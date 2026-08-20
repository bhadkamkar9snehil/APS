using APS.Application;
using APS.Domain;
using Google.OrTools.Sat;

namespace APS.Planning;

public sealed class FiniteScheduleOptimizer : IFiniteScheduleOptimizer
{
    public FiniteScheduleResult Solve(FiniteScheduleRequest request) => Solve(request, explainInfeasibility: true);

    private FiniteScheduleResult Solve(FiniteScheduleRequest request, bool explainInfeasibility)
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
        var schedulableResources = request.Resources
            .Where(IsSchedulable)
            .ToDictionary(r => r.Id);
        var intervalsByResource = schedulableResources.Keys
            .ToDictionary(id => id, _ => new List<IntervalVar>());

        // Cumulative resources carry a demand alongside every interval; the two lists stay index-aligned.
        var demandsByResource = schedulableResources
            .Where(pair => pair.Value.SchedulingMode == ResourceSchedulingMode.Cumulative)
            .ToDictionary(pair => pair.Key, _ => new List<long>());
        var capacityByResource = new Dictionary<Guid, long>();
        foreach (var resourceId in demandsByResource.Keys)
        {
            var resource = schedulableResources[resourceId];
            var capacity = ResourceCapacityModel.EffectiveCapacityUnits(resource);
            if (capacity <= 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "RESOURCE_CUMULATIVE_CAPACITY_MISSING",
                    $"Resource {resource.Code} is configured Cumulative but has no usable NominalConcurrentCapacity.",
                    resource.Id));
            }
            capacityByResource[resourceId] = capacity;
        }

        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

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
                if (!intervalsByResource.TryGetValue(option.ResourceId, out var resourceIntervals)) continue;

                var resource = schedulableResources[option.ResourceId];
                long demand = 0;
                if (demandsByResource.TryGetValue(option.ResourceId, out var resourceDemands))
                {
                    demand = ResourceCapacityModel.DemandUnits(resource, task.QuantityMt, option.CapacityDemand);
                    if (demand > capacityByResource[option.ResourceId])
                    {
                        // The task cannot physically fit this unit even on its own. Drop the option
                        // rather than handing CP-SAT a model that can only come back INFEASIBLE; if it
                        // was the only option the TASK_WITHOUT_ACTIVE_RESOURCE check below reports it.
                        issues.Add(new PlanningIssue(
                            PlanningIssueSeverity.Warning,
                            "TASK_DEMAND_EXCEEDS_RESOURCE_CAPACITY",
                            $"Task {task.Name} demands more capacity than resource {resource.Code} offers and cannot run there.",
                            task.TaskId));
                        continue;
                    }
                    resourceDemands.Add(demand);
                }

                var selected = model.NewBoolVar($"task_{task.TaskId:N}_resource_{option.ResourceId:N}");
                presence[option.ResourceId] = selected;
                var duration = Math.Max(1, option.DurationMinutes);
                resourceIntervals.Add(model.NewOptionalIntervalVar(
                    start,
                    duration,
                    end,
                    selected,
                    $"interval_{task.TaskId:N}_{option.ResourceId:N}"));
            }

            if (presence.Count == 0)
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "TASK_WITHOUT_ACTIVE_RESOURCE",
                    $"Task {task.Name} has no schedulable resource option after operating-state filtering.",
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

        foreach (var calendar in request.ResourceCalendars)
        {
            if (!intervalsByResource.TryGetValue(calendar.ResourceId, out var intervals)) continue;
            var start = Math.Clamp(ToMinute(calendar.Start, request.HorizonStartUtc, horizonMinutes), 0, horizonMinutes);
            var end = Math.Clamp(ToMinute(calendar.End, request.HorizonStartUtc, horizonMinutes), 0, horizonMinutes);
            if (end <= start) continue;

            if (!demandsByResource.TryGetValue(calendar.ResourceId, out var calendarDemands))
            {
                // Disjunctive: an unavailable window is a block that owns the machine outright. A
                // window that only derates capacity cannot be expressed against a unary machine, so
                // it correctly changes nothing here.
                if (!calendar.IsAvailable)
                {
                    intervals.Add(model.NewFixedSizeIntervalVar(start, end - start, $"calendar_block_{calendar.Id:N}"));
                }
                continue;
            }

            // Cumulative: an outage consumes the whole capacity, a derate consumes the part that is
            // no longer offered. Both are just a fixed interval with the right demand.
            var resource = schedulableResources[calendar.ResourceId];
            var fullCapacity = capacityByResource[calendar.ResourceId];
            var remainingCapacity = calendar.IsAvailable
                ? ResourceCapacityModel.EffectiveCapacityUnits(
                    resource,
                    resource.CapacityFactorPct * (calendar.CapacityFactorPct ?? 100m) / 100m)
                : 0;
            var blockedCapacity = fullCapacity - Math.Clamp(remainingCapacity, 0, fullCapacity);
            if (blockedCapacity <= 0) continue;

            intervals.Add(model.NewFixedSizeIntervalVar(start, end - start, $"calendar_block_{calendar.Id:N}"));
            calendarDemands.Add(blockedCapacity);
        }

        foreach (var (resourceId, intervals) in intervalsByResource)
        {
            if (demandsByResource.TryGetValue(resourceId, out var demands))
            {
                if (intervals.Count == 0) continue;
                model.AddCumulative(capacityByResource[resourceId]).AddDemands(intervals, demands);
                continue;
            }

            if (intervals.Count > 1) model.AddNoOverlap(intervals);
        }

        ApplyDependencies(request, model, taskVars, issues);
        ApplyLinkedResourceGroups(request, model, taskVars);
        ApplyMaterialReservoirs(request, horizonMinutes, model, taskVars, issues);
        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var objectiveTerms = new List<LinearExpr>();
        ApplyResourceSequenceCircuits(request, model, taskVars, schedulableResources, objectiveTerms);
        ApplyStabilityConstraints(request, horizonMinutes, model, taskVars, objectiveTerms, issues);

        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var obligationsByTask = (request.ServiceObligations ?? Array.Empty<FiniteScheduleServiceObligation>())
            .Where(x => x.QuantityMt > 0m)
            .GroupBy(x => x.TaskId)
            .ToDictionary(x => x.Key, x => x.ToArray());

        foreach (var obligation in request.ServiceObligations ?? Array.Empty<FiniteScheduleServiceObligation>())
        {
            if (taskVars.ContainsKey(obligation.TaskId)) continue;
            issues.Add(new PlanningIssue(
                PlanningIssueSeverity.Error,
                "SERVICE_OBLIGATION_TASK_NOT_FOUND",
                $"Service obligation for Production Order {obligation.ProductionOrderId} references missing task {obligation.TaskId}.",
                obligation.TaskId));
        }
        if (issues.Any(i => i.Severity == PlanningIssueSeverity.Error))
        {
            return new FiniteScheduleResult("InvalidInput", false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        foreach (var task in request.Tasks)
        {
            if (!taskVars.TryGetValue(task.TaskId, out var variables)) continue;

            if (obligationsByTask.TryGetValue(task.TaskId, out var obligations) && obligations.Length > 0)
            {
                for (var i = 0; i < obligations.Length; i++)
                {
                    var obligation = obligations[i];
                    var dueMinute = ToMinute(obligation.DueUtc, request.HorizonStartUtc, horizonMinutes);
                    var tardiness = model.NewIntVar(0, horizonMinutes, $"late_{task.TaskId:N}_{i:000}");
                    model.Add(tardiness >= variables.End - dueMinute);

                    // 0.1 MT service units keep the objective quantity-sensitive without excessive coefficients.
                    var quantityUnits = Math.Max(1L, (long)Math.Ceiling((double)(obligation.QuantityMt * 10m)));
                    var priorityWeight = Math.Max(1L, obligation.Priority + 1L);
                    objectiveTerms.Add(tardiness * (quantityUnits * priorityWeight * 100L));
                }
            }
            else if (task.DueUtc.HasValue)
            {
                // Compatibility fallback for tasks with no explicit PO allocation obligation.
                var dueMinute = ToMinute(task.DueUtc.Value, request.HorizonStartUtc, horizonMinutes);
                var tardiness = model.NewIntVar(0, horizonMinutes, $"late_{task.TaskId:N}");
                model.Add(tardiness >= variables.End - dueMinute);
                objectiveTerms.Add(tardiness * (Math.Max(1, task.Priority + 1) * 1000L));
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
                    ? "No finite schedule satisfies the current resource, calendar, time-phased material, dependency, sequencing and time-fence constraints."
                    : $"CP-SAT returned {status}."));

            // Naming every constraint family at once tells a planner nothing about which one to change.
            // Probing recovers the family that is actually binding (#19). Only for a genuine
            // infeasibility - a solver timeout means nothing has been proven impossible.
            if (explainInfeasibility && status == CpSolverStatus.Infeasible)
            {
                issues.AddRange(ScheduleInfeasibilityDiagnostician.Explain(
                    request,
                    probe => Solve(probe, explainInfeasibility: false)));
            }

            return new FiniteScheduleResult(status.ToString(), false, 0, Array.Empty<FiniteScheduleAssignment>(), issues);
        }

        var assignments = new List<FiniteScheduleAssignment>();
        foreach (var task in request.Tasks)
        {
            if (!taskVars.TryGetValue(task.TaskId, out var variables)) continue;
            var resourceId = variables.Presence.First(pair => solver.Value(pair.Value) == 1).Key;
            assignments.Add(new FiniteScheduleAssignment(
                task.TaskId,
                task.SourceEntityId,
                resourceId,
                request.HorizonStartUtc.AddMinutes(solver.Value(variables.Start)),
                request.HorizonStartUtc.AddMinutes(solver.Value(variables.End))));
        }

        return new FiniteScheduleResult(
            status.ToString(),
            true,
            Convert.ToInt64(Math.Round(solver.ObjectiveValue)),
            assignments.OrderBy(a => a.StartUtc).ToArray(),
            issues);
    }

    private static void ApplyDependencies(
        FiniteScheduleRequest request,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars,
        ICollection<PlanningIssue> issues)
    {
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

                if (dependency.AllowedResourcePairs is not { Count: > 0 })
                {
                    model.Add(current.Start >= predecessor.End + Math.Max(0, dependency.MinimumLagMinutes));
                    if (dependency.MaximumLagMinutes.HasValue)
                    {
                        model.Add(current.Start <= predecessor.End + Math.Max(0, dependency.MaximumLagMinutes.Value));
                    }
                    continue;
                }

                var allowedPairs = dependency.AllowedResourcePairs
                    .ToDictionary(x => (x.PredecessorResourceId, x.SuccessorResourceId));

                foreach (var predecessorPresence in predecessor.Presence)
                {
                    foreach (var currentPresence in current.Presence)
                    {
                        if (!allowedPairs.TryGetValue((predecessorPresence.Key, currentPresence.Key), out var pair))
                        {
                            model.Add(predecessorPresence.Value + currentPresence.Value <= 1);
                            continue;
                        }

                        var minimum = model.Add(current.Start >= predecessor.End + Math.Max(0, pair.MinimumLagMinutes));
                        minimum.OnlyEnforceIf(predecessorPresence.Value);
                        minimum.OnlyEnforceIf(currentPresence.Value);

                        if (pair.MaximumLagMinutes.HasValue)
                        {
                            var maximum = model.Add(current.Start <= predecessor.End + Math.Max(0, pair.MaximumLagMinutes.Value));
                            maximum.OnlyEnforceIf(predecessorPresence.Value);
                            maximum.OnlyEnforceIf(currentPresence.Value);
                        }
                    }
                }
            }
        }
    }

    private static void ApplyLinkedResourceGroups(
        FiniteScheduleRequest request,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars)
    {
        // Ties consecutive tasks' per-resource presence together so the group resolves to one physical
        // resource, even though each task still individually picks from its own eligible-resource set
        // (#16) - e.g. every heat in one continuous cast sequence must land on the same physical CCM.
        // Equality is transitive, so linking each consecutive pair links the whole group.
        foreach (var group in request.LinkedResourceTaskGroups ?? Array.Empty<IReadOnlyCollection<Guid>>())
        {
            var ordered = group.Where(taskVars.ContainsKey).ToArray();
            for (var i = 0; i < ordered.Length - 1; i++)
            {
                var current = taskVars[ordered[i]];
                var next = taskVars[ordered[i + 1]];
                foreach (var resourceId in current.Presence.Keys.Intersect(next.Presence.Keys))
                {
                    model.Add(current.Presence[resourceId] == next.Presence[resourceId]);
                }
            }
        }
    }

    private static void ApplyMaterialReservoirs(
        FiniteScheduleRequest request,
        int horizonMinutes,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars,
        ICollection<PlanningIssue> issues)
    {
        foreach (var pool in (request.MaterialEvents ?? Array.Empty<ScheduledMaterialEvent>())
                     .GroupBy(x => x.MaterialPoolKey, StringComparer.OrdinalIgnoreCase))
        {
            var events = pool.ToArray();
            if (events.Length == 0) continue;
            var totalPositive = events.Where(x => x.QuantityDeltaKg > 0).Sum(x => x.QuantityDeltaKg);
            if (totalPositive <= 0 && events.Any(x => x.QuantityDeltaKg < 0))
            {
                issues.Add(new PlanningIssue(
                    PlanningIssueSeverity.Error,
                    "MATERIAL_POOL_WITHOUT_SUPPLY",
                    $"Material pool {pool.Key} has consumption but no planned or confirmed supply."));
                continue;
            }

            var reservoir = model.AddReservoirConstraint(0, Math.Max(0, totalPositive));
            foreach (var materialEvent in events)
            {
                switch (materialEvent.Timing)
                {
                    case ScheduledMaterialEventTiming.FixedTime:
                        var fixedMinute = ToMinute(
                            materialEvent.FixedTimeUtc ?? request.HorizonStartUtc,
                            request.HorizonStartUtc,
                            horizonMinutes);
                        reservoir.AddEvent(fixedMinute, materialEvent.QuantityDeltaKg);
                        break;

                    case ScheduledMaterialEventTiming.TaskStart:
                    case ScheduledMaterialEventTiming.TaskEnd:
                        if (!materialEvent.TaskId.HasValue || !taskVars.TryGetValue(materialEvent.TaskId.Value, out var task))
                        {
                            issues.Add(new PlanningIssue(
                                PlanningIssueSeverity.Error,
                                "MATERIAL_EVENT_TASK_NOT_FOUND",
                                $"Material event for pool {pool.Key} references missing task {materialEvent.TaskId}."));
                            continue;
                        }
                        reservoir.AddEvent(
                            materialEvent.Timing == ScheduledMaterialEventTiming.TaskStart ? task.Start : task.End,
                            materialEvent.QuantityDeltaKg);
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }
    }

    private static void ApplyResourceSequenceCircuits(
        FiniteScheduleRequest request,
        CpModel model,
        IReadOnlyDictionary<Guid, TaskVariables> taskVars,
        IReadOnlyDictionary<Guid, Resource> resources,
        ICollection<LinearExpr> objectiveTerms)
    {
        // One completely independent circuit is created for each physical ResourceId.
        // Optional self-loops remove tasks that CP-SAT assigns to another eligible resource.
        foreach (var resource in resources.Values)
        {
            // A circuit imposes a total order on everything the resource runs, which would serialize a
            // cumulative resource through the back door and defeat the point of modelling it that way.
            // Resources that simply hold material also have no meaningful "previous job" to change over from.
            if (resource.SchedulingMode == ResourceSchedulingMode.Cumulative || !resource.AppliesSequenceRules) continue;

            var nodes = request.Tasks
                .Where(task => taskVars.TryGetValue(task.TaskId, out var variables) &&
                               variables.Presence.ContainsKey(resource.Id))
                .Select((task, index) => new ResourceSequenceNode(index + 1, task))
                .ToArray();
            if (nodes.Length <= 1) continue;

            var circuit = model.AddCircuit();
            var presences = nodes.Select(node => taskVars[node.Task.TaskId].Presence[resource.Id]).ToArray();
            var resourceUnused = model.NewBoolVar($"seq_{resource.Id:N}_unused");
            circuit.AddArc(0, 0, resourceUnused);
            var unusedConstraint = model.Add(LinearExpr.Sum(presences) == 0);
            unusedConstraint.OnlyEnforceIf(resourceUnused);
            var usedConstraint = model.Add(LinearExpr.Sum(presences) >= 1);
            usedConstraint.OnlyEnforceIf(resourceUnused.Not());

            foreach (var node in nodes)
            {
                var presence = taskVars[node.Task.TaskId].Presence[resource.Id];
                circuit.AddArc(node.NodeIndex, node.NodeIndex, presence.Not());
                circuit.AddArc(0, node.NodeIndex, model.NewBoolVar($"seq_{resource.Id:N}_0_{node.NodeIndex}"));
                circuit.AddArc(node.NodeIndex, 0, model.NewBoolVar($"seq_{resource.Id:N}_{node.NodeIndex}_0"));
            }

            foreach (var previous in nodes)
            {
                foreach (var current in nodes)
                {
                    if (previous.NodeIndex == current.NodeIndex) continue;

                    var transition = previous.Task.SourceEntityId == current.Task.SourceEntityId
                        ? new TransitionProfile(true, 0, 0)
                        : ResolveTransition(request.TransitionRules, resource, previous.Task, current.Task);
                    if (!transition.IsAllowed) continue;

                    var adjacency = model.NewBoolVar($"seq_{resource.Id:N}_{previous.NodeIndex}_{current.NodeIndex}");
                    circuit.AddArc(previous.NodeIndex, current.NodeIndex, adjacency);
                    model.Add(taskVars[current.Task.TaskId].Start >= taskVars[previous.Task.TaskId].End + transition.TransitionMinutes)
                        .OnlyEnforceIf(adjacency);
                    if (transition.Penalty > 0) objectiveTerms.Add(adjacency * transition.Penalty);
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
                FindTransitionRule(rules, resource, previous, current, TransitionDimension.Grade, previous.GradeCode, current.GradeCode),
                FindTransitionRule(rules, resource, previous, current, TransitionDimension.CrossSection, previous.CrossSectionCode, current.CrossSectionCode)
            }
            .Where(rule => rule is not null)
            .Cast<TransitionRule>()
            .ToArray();

        if (matchedRules.Any(rule => !rule.IsAllowed || rule.RequiresSequenceBreak))
        {
            return new TransitionProfile(false, 0, 0);
        }

        return new TransitionProfile(
            true,
            matchedRules.Select(rule => Minutes(rule.TransitionTime)).DefaultIfEmpty(0).Max(),
            matchedRules.Sum(rule => Math.Max(0, rule.Penalty)));
    }

    private static TransitionRule? FindTransitionRule(
        IReadOnlyCollection<TransitionRule> rules,
        Resource resource,
        FiniteScheduleTask previous,
        FiniteScheduleTask current,
        TransitionDimension dimension,
        string from,
        string to) =>
        rules
            .Where(rule =>
                rule.Dimension == dimension &&
                string.Equals(rule.FromCode, from, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.ToCode, to, StringComparison.OrdinalIgnoreCase))
            .Where(rule =>
                (!rule.ResourceId.HasValue || rule.ResourceId == resource.Id) &&
                (!rule.ResourceType.HasValue || rule.ResourceType == resource.ResourceType) &&
                (!rule.ProcessUnitType.HasValue || rule.ProcessUnitType == resource.ProcessUnitType) &&
                (!rule.ProcessOperationType.HasValue ||
                 rule.ProcessOperationType == current.ProcessOperationType ||
                 rule.ProcessOperationType == previous.ProcessOperationType))
            .OrderByDescending(rule => rule.ResourceId == resource.Id)
            .ThenByDescending(rule => rule.ProcessUnitType == resource.ProcessUnitType)
            .ThenByDescending(rule => rule.ProcessOperationType.HasValue)
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

    private static bool IsSchedulable(Resource resource) =>
        resource.IsActive && resource.OperatingState is
            ResourceOperatingState.Available or
            ResourceOperatingState.CapacityDerated or
            ResourceOperatingState.QualityRestricted;

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

    private sealed record ResourceSequenceNode(int NodeIndex, FiniteScheduleTask Task);
    private sealed record TransitionProfile(bool IsAllowed, int TransitionMinutes, int Penalty);
}
