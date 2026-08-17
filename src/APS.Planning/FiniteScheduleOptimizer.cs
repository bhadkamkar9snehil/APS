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
        ApplyResourceSequenceCircuits(request, horizonMinutes, model, taskVars, objectiveTerms, issues);
        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        ApplyStabilityConstraints(request, horizonMinutes, model, taskVars, objectiveTerms, issues);
        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

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
                    ? "No finite schedule satisfies the current resource, calendar, dependency, sequencing and time-fence constraints."
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

    private static void ApplyResourceSequenceCircuits(
        FiniteScheduleRequest request,
        int horizonMinutes,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars,
        ICollection<LinearExpr> objectiveTerms,
        ICollection<PlanningIssue> issues)
    {
        var resources = request.Resources
            .Where(resource => resource.IsActive)
            .ToDictionary(resource => resource.Id);

        // Phase 1 deliberately sequences only tasks whose physical resource is already fixed.
        // Each ResourceId receives its own independent circuit. Resources of the same type are
        // never pooled, so CCM-1/CCM-2 and RM-1/RM-2 remain independently schedulable in parallel.
        var fixedTasksByResource = request.Tasks
            .Where(task =>
                task.ResourceOptions.Count == 1 &&
                taskVars.ContainsKey(task.TaskId))
            .GroupBy(task => task.ResourceOptions.Single().ResourceId);

        foreach (var resourceTasks in fixedTasksByResource)
        {
            if (!resources.TryGetValue(resourceTasks.Key, out var resource))
            {
                continue;
            }

            var groups = new List<ResourceSequenceGroup>();
            var sourceGroups = resourceTasks
                .GroupBy(task => task.SourceEntityId)
                .ToArray();

            foreach (var sourceGroup in sourceGroups)
            {
                var tasks = sourceGroup.ToArray();
                var gradeCodes = tasks
                    .Select(task => task.GradeCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var sectionCodes = tasks
                    .Select(task => task.CrossSectionCode)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (gradeCodes.Length != 1 || sectionCodes.Length != 1)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "SEQUENCE_SOURCE_ATTRIBUTES_AMBIGUOUS",
                        $"Source {sourceGroup.Key} has multiple grade/section identities on resource {resource.Code}; it cannot be represented as one sequencing node.",
                        sourceGroup.Key));
                    continue;
                }

                var groupStart = model.NewIntVar(
                    0,
                    horizonMinutes,
                    $"seq_group_start_{resource.Id:N}_{sourceGroup.Key:N}");
                var groupEnd = model.NewIntVar(
                    0,
                    horizonMinutes,
                    $"seq_group_end_{resource.Id:N}_{sourceGroup.Key:N}");

                model.AddMinEquality(groupStart, tasks.Select(task => taskVars[task.TaskId].Start));
                model.AddMaxEquality(groupEnd, tasks.Select(task => taskVars[task.TaskId].End));

                groups.Add(new ResourceSequenceGroup(
                    groups.Count + 1,
                    sourceGroup.Key,
                    tasks[0],
                    groupStart,
                    groupEnd));
            }

            if (groups.Count <= 1)
            {
                continue;
            }

            var circuit = model.AddCircuit();

            // Node 0 is a dummy depot. Arcs to/from it make the circuit represent a linear
            // machine queue: exactly N-1 real plan-to-plan adjacencies are selected for N groups.
            foreach (var group in groups)
            {
                var firstArc = model.NewBoolVar($"seq_{resource.Id:N}_0_{group.NodeIndex}");
                var lastArc = model.NewBoolVar($"seq_{resource.Id:N}_{group.NodeIndex}_0");
                circuit.AddArc(0, group.NodeIndex, firstArc);
                circuit.AddArc(group.NodeIndex, 0, lastArc);
            }

            foreach (var previous in groups)
            {
                foreach (var current in groups)
                {
                    if (previous.NodeIndex == current.NodeIndex) continue;

                    var transition = ResolveTransition(
                        request.TransitionRules,
                        resource,
                        previous.RepresentativeTask,
                        current.RepresentativeTask);
                    if (!transition.IsAllowed)
                    {
                        // No arc means this directional adjacency cannot be selected.
                        continue;
                    }

                    var adjacency = model.NewBoolVar(
                        $"seq_{resource.Id:N}_{previous.NodeIndex}_{current.NodeIndex}");
                    circuit.AddArc(previous.NodeIndex, current.NodeIndex, adjacency);

                    model.Add(current.Start >= previous.End + transition.TransitionMinutes)
                        .OnlyEnforceIf(adjacency);

                    if (transition.Penalty > 0)
                    {
                        objectiveTerms.Add(adjacency * transition.Penalty);
                    }
                }
            }
        }
    }

    private static TransitionProfile ResolveTransition(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        FiniteScheduleTask previous,
        FiniteScheduleTask current)
    {
        var matchedRules = new[]
            {
                FindTransitionRule(
                    rules,
                    resource,
                    TransitionDimension.Grade,
                    previous.GradeCode,
                    current.GradeCode),
                FindTransitionRule(
                    rules,
                    resource,
                    TransitionDimension.CrossSection,
                    previous.CrossSectionCode,
                    current.CrossSectionCode)
            }
            .Where(rule => rule is not null)
            .Cast<TransitionRule>()
            .ToArray();

        if (matchedRules.Any(rule => !rule.IsAllowed))
        {
            return new TransitionProfile(false, 0, 0);
        }

        var transitionMinutes = matchedRules
            .Select(rule => Minutes(rule.TransitionTime))
            .DefaultIfEmpty(0)
            .Max();
        var penalty = matchedRules.Sum(rule => Math.Max(0, rule.Penalty));

        return new TransitionProfile(true, transitionMinutes, penalty);
    }

    private static TransitionRule? FindTransitionRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(rule =>
                rule.Dimension == dimension &&
                string.Equals(rule.FromCode, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.ToCode, to, StringComparison.OrdinalIgnoreCase))
            .Where(rule =>
                rule.ResourceId == resource.Id ||
                rule.ResourceType == resource.ResourceType ||
                (!rule.ResourceId.HasValue && !rule.ResourceType.HasValue))
            .OrderByDescending(rule => rule.ResourceId == resource.Id)
            .ThenByDescending(rule => rule.ResourceType == resource.ResourceType)
            .FirstOrDefault();

    private static void ApplyStabilityConstraints(
        FiniteScheduleRequest request,
        int horizonMinutes,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars,
        ICollection<LinearExpr> objectiveTerms,
        ICollection<PlanningIssue> issues)
    {
        foreach (var constraint in request.StabilityConstraints ?? Array.Empty<FiniteScheduleStabilityConstraint>())
        {
            if (!taskVars.TryGetValue(constraint.TaskId, out var variables)) continue;

            if (!variables.Presence.TryGetValue(constraint.BaselineResourceId, out var baselineResource))
            {
                if (constraint.Zone == TimeFenceZone.Frozen)
                {
                    issues.Add(new PlanningIssue(
                        PlanningIssueSeverity.Error,
                        "FROZEN_RESOURCE_NO_LONGER_ELIGIBLE",
                        $"Frozen task {constraint.TaskId} cannot remain on baseline resource {constraint.BaselineResourceId}.",
                        constraint.TaskId));
                }
                continue;
            }

            var baselineStart = ToMinute(constraint.BaselineStartUtc, request.HorizonStartUtc, horizonMinutes);
            if (constraint.Zone == TimeFenceZone.Frozen)
            {
                model.Add(variables.Start == baselineStart);
                model.Add(baselineResource == 1);
                continue;
            }

            if (constraint.Zone != TimeFenceZone.Slushy) continue;

            var delta = model.NewIntVar(-horizonMinutes, horizonMinutes, $"move_delta_{constraint.TaskId:N}");
            var absoluteDelta = model.NewIntVar(0, horizonMinutes, $"move_abs_{constraint.TaskId:N}");
            model.Add(delta == variables.Start - baselineStart);
            model.AddAbsEquality(absoluteDelta, delta);
            objectiveTerms.Add(absoluteDelta * Math.Max(0, constraint.MovementPenaltyPerMinute));

            if (constraint.ResourceChangePenalty > 0)
            {
                var changedResource = model.NewBoolVar($"resource_changed_{constraint.TaskId:N}");
                model.Add(changedResource + baselineResource == 1);
                objectiveTerms.Add(changedResource * constraint.ResourceChangePenalty);
            }
        }
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

    private sealed record ResourceSequenceGroup(
        int NodeIndex,
        Guid SourceEntityId,
        FiniteScheduleTask RepresentativeTask,
        IntVar Start,
        IntVar End);

    private sealed record TransitionProfile(
        bool IsAllowed,
        int TransitionMinutes,
        int Penalty);
}