using APS.Application;
using APS.Domain;
using Google.OrTools.Sat;

namespace APS.Planning;

public sealed class FiniteScheduleOptimizer : IFiniteScheduleOptimizer
{
    public FiniteScheduleResult Solve(FiniteScheduleRequest request)
    {
        var issues = new List<PlanningIssue>();
        if (request.HorizonEndUtc <= request.HorizonStartUtc)
        {
            return Invalid("SCHEDULE_HORIZON_INVALID", "Scheduling horizon end must be after its start.");
        }

        if (request.Tasks.Count == 0)
        {
            return new FiniteScheduleResult("Empty", true, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var horizonMinutes = Math.Max(1, Minutes(request.HorizonEndUtc - request.HorizonStartUtc));
        var model = new CpModel();
        var taskVars = new Dictionary<Guid, TaskVariables>();
        var intervalsByResource = request.Resources
            .Where(r => r.IsActive)
            .ToDictionary(r => r.Id, _ => new List<IntervalVar>());

        foreach (var task in request.Tasks)
        {
            if (task.ResourceOptions.Count == 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "TASK_WITHOUT_RESOURCE",
                    $"Task {task.Name} has no eligible resource.",
                    task.TaskId));
                continue;
            }

            var start = model.NewIntVar(0, horizonMinutes, $"start_{task.TaskId:N}");
            var end = model.NewIntVar(0, horizonMinutes, $"end_{task.TaskId:N}");
            var presence = new Dictionary<Guid, BoolVar>();

            foreach (var option in task.ResourceOptions)
            {
                if (!intervalsByResource.ContainsKey(option.ResourceId))
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "RESOURCE_NOT_AVAILABLE",
                        $"Task {task.Name} references resource {option.ResourceId} that is not active in the scheduling request.",
                        task.TaskId));
                    continue;
                }

                var selected = model.NewBoolVar($"task_{task.TaskId:N}_resource_{option.ResourceId:N}");
                presence[option.ResourceId] = selected;
                var duration = Math.Max(1, option.DurationMinutes);
                var interval = model.NewOptionalIntervalVar(
                    start,
                    duration,
                    end,
                    selected,
                    $"interval_{task.TaskId:N}_{option.ResourceId:N}");
                intervalsByResource[option.ResourceId].Add(interval);
            }

            if (presence.Count == 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "TASK_WITHOUT_ACTIVE_RESOURCE",
                    $"Task {task.Name} has no active resource option.",
                    task.TaskId));
                continue;
            }

            model.AddExactlyOne(presence.Values.Cast<ILiteral>());

            if (task.EarliestStartUtc.HasValue)
            {
                model.Add(start >= ToMinute(task.EarliestStartUtc.Value, request.HorizonStartUtc, horizonMinutes));
            }

            taskVars[task.TaskId] = new TaskVariables(start, end, presence);
        }

        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        // Resource calendars are represented as fixed unavailable intervals inside the same NoOverlap constraint.
        foreach (var calendar in request.ResourceCalendars.Where(c => !c.IsAvailable))
        {
            if (!intervalsByResource.TryGetValue(calendar.ResourceId, out var intervals)) continue;
            var start = Math.Clamp(ToMinute(calendar.Start, request.HorizonStartUtc, horizonMinutes), 0, horizonMinutes);
            var end = Math.Clamp(ToMinute(calendar.End, request.HorizonStartUtc, horizonMinutes), 0, horizonMinutes);
            if (end <= start) continue;

            intervals.Add(model.NewFixedSizeIntervalVar(
                start,
                end - start,
                $"calendar_block_{calendar.Id:N}"));
        }

        foreach (var intervals in intervalsByResource.Values)
        {
            if (intervals.Count > 1)
            {
                model.AddNoOverlap(intervals);
            }
        }

        foreach (var task in request.Tasks)
        {
            if (!taskVars.TryGetValue(task.TaskId, out var current)) continue;

            foreach (var dependency in task.Dependencies)
            {
                if (!taskVars.TryGetValue(dependency.PredecessorTaskId, out var predecessor))
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "DEPENDENCY_NOT_FOUND",
                        $"Task {task.Name} references missing predecessor {dependency.PredecessorTaskId}.",
                        task.TaskId));
                    continue;
                }

                model.Add(current.Start >= predecessor.End + Math.Max(0, dependency.MinimumLagMinutes));
                if (dependency.MaximumLagMinutes.HasValue)
                {
                    model.Add(current.Start <= predecessor.End + Math.Max(0, dependency.MaximumLagMinutes.Value));
                }
            }
        }

        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var objectiveTerms = new List<LinearExpr>();
        foreach (var task in request.Tasks)
        {
            if (!taskVars.TryGetValue(task.TaskId, out var variables)) continue;

            if (task.DueUtc.HasValue)
            {
                var dueMinute = ToMinute(task.DueUtc.Value, request.HorizonStartUtc, horizonMinutes);
                var tardiness = model.NewIntVar(0, horizonMinutes, $"late_{task.TaskId:N}");
                model.Add(tardiness >= variables.End - dueMinute);
                var weight = Math.Max(1, task.Priority + 1) * 1000L;
                objectiveTerms.Add(tardiness * weight);
            }

            foreach (var option in task.ResourceOptions.Where(o => o.AssignmentPenalty > 0))
            {
                if (variables.Presence.TryGetValue(option.ResourceId, out var selected))
                {
                    objectiveTerms.Add(selected * option.AssignmentPenalty);
                }
            }
        }

        var makespan = model.NewIntVar(0, horizonMinutes, "makespan");
        model.AddMaxEquality(makespan, taskVars.Values.Select(v => v.End));
        objectiveTerms.Add(makespan);
        model.Minimize(LinearExpr.Sum(objectiveTerms));

        var validation = model.Validate();
        if (!string.IsNullOrWhiteSpace(validation))
        {
            issues.Add(new PlanningIssue(PlanningIssueSeverity.Error, "CP_MODEL_INVALID", validation));
            return new FiniteScheduleResult("ModelInvalid", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var solver = new CpSolver
        {
            StringParameters = $"max_time_in_seconds:{Math.Max(1, request.MaxSolverSeconds)} num_search_workers:8"
        };
        var status = solver.Solve(model);
        var feasible = status is CpSolverStatus.Optimal or CpSolverStatus.Feasible;

        if (!feasible)
        {
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                status == CpSolverStatus.Infeasible ? "SCHEDULE_INFEASIBLE" : "SCHEDULE_NOT_SOLVED",
                status == CpSolverStatus.Infeasible
                    ? "No finite schedule satisfies the current resource, calendar and dependency constraints."
                    : $"CP-SAT returned {status}."));
            return new FiniteScheduleResult(status.ToString(), false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var assignments = new List<FiniteScheduleAssignment>();
        foreach (var task in request.Tasks)
        {
            if (!taskVars.TryGetValue(task.TaskId, out var variables)) continue;
            var resourceId = variables.Presence.First(pair => solver.Value(pair.Value) == 1).Key;
            var startMinute = solver.Value(variables.Start);
            var endMinute = solver.Value(variables.End);

            assignments.Add(new FiniteScheduleAssignment(
                task.TaskId,
                task.SourceEntityId,
                resourceId,
                request.HorizonStartUtc.AddMinutes(startMinute),
                request.HorizonStartUtc.AddMinutes(endMinute)));
        }

        return new FiniteScheduleResult(
            status.ToString(),
            true,
            Convert.ToInt64(Math.Round(solver.ObjectiveValue)),
            assignments.OrderBy(a => a.StartUtc).ToArray(),
            issues);
    }

    private static FiniteScheduleResult Invalid(string code, string message) =>
        new("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(),
            new[] { new PlanningIssue(PlanningIssueSeverity.Error, code, message) });

    private static int ToMinute(DateTime value, DateTime origin, int horizonMinutes)
    {
        var minute = (int)Math.Floor((value - origin).TotalMinutes);
        return Math.Clamp(minute, 0, horizonMinutes);
    }

    private static int Minutes(TimeSpan value) => Math.Max(0, (int)Math.Ceiling(value.TotalMinutes));

    private sealed record TaskVariables(
        IntVar Start,
        IntVar End,
        IReadOnlyDictionary<Guid, BoolVar> Presence);
}
