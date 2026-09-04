using APS.Application;
using APS.Domain;

namespace APS.UI.State;

/// <summary>
/// Planner-owned controls for the next Calculate/Replan operation. These are planning-policy levers,
/// not a second copy of technical master data: routes, capabilities, calendars, grade requirements and
/// thermal envelopes remain authoritative in Master Data and operating scenarios.
/// </summary>
public sealed class PlannerConstraintState
{
    public decimal NominalHeatSizeMt { get; set; } = 90m;
    public decimal MinimumHeatSizeMt { get; set; } = 63m;
    public decimal MaximumHeatSizeMt { get; set; } = 103.5m;
    public decimal TargetCampaignQuantityMt { get; set; } = 600m;
    public decimal MaximumCampaignQuantityMt { get; set; } = 600m;
    public bool AllowMtoMtsMixing { get; set; } = true;
    public bool AllowMixedGradesWithinSequenceClass { get; set; } = true;
    public decimal ExpectedCastingYieldPct { get; set; } = 100m;

    public decimal ServiceRiskPerMtDay { get; set; } = 1000m;
    public decimal EarlyProductionPerMtDay { get; set; } = 1m;
    public decimal CampaignSetupCost { get; set; } = 40m;
    public decimal ResidualHeatPerMt { get; set; } = 4m;
    public decimal BelowMinimumCampaignPerMt { get; set; } = 8m;
    public decimal GradeTransitionCostWeight { get; set; } = 1m;
    public decimal HeatTargetDeviationPerMt { get; set; } = 1m;
    public decimal CampaignStabilityChangePerMt { get; set; } = 1m;

    public int MaximumHeatsPerCastSequence { get; set; } = 8;
    public int DefaultCastingMinutesPerHeat { get; set; } = 55;
    public int SequenceBreakPenalty { get; set; } = 500;
    public decimal CastingYieldPct { get; set; } = 100m;
    public int DefaultRollingMinutesPer100Mt { get; set; } = 120;
    public bool AllowCrossCampaignCastSequences { get; set; } = true;
    public bool AllowCrossCampaignRollingPlans { get; set; } = true;

    public int FrozenMinutes { get; set; } = 120;
    public int SlushyMinutes { get; set; } = 720;
    public int SlushyMovementPenaltyPerMinute { get; set; } = 50;
    public int SlushyResourceChangePenalty { get; set; } = 5000;

    public int MaxSolverSeconds { get; set; } = 20;
    public string? ScenarioCode { get; set; }

    public bool UseAssignmentCommitmentPolicy { get; set; }
    public int FirmMinutesBeforeStart { get; set; } = 120;
    public int CommitMinutesBeforeStart { get; set; } = 30;
    public bool AllowRedispatchWhenFirm { get; set; } = true;
    public bool AllowRedispatchWhenCommittedForDisruption { get; set; } = true;
    public bool CommitWhenPredecessorRunning { get; set; }
    public bool CommitWhenPredecessorCompleted { get; set; }
    public bool RequireDispatchAcknowledgement { get; set; }

    public int RepairSuccessorDepth { get; set; } = 4;
    public int RepairHorizonMinutes { get; set; } = 720;
    public bool FreezeUnaffectedOperations { get; set; } = true;
    public bool IncludeSameResourceNeighbors { get; set; } = true;

    public PlanningCalculationRequest BuildCalculationRequest(DateTime horizonStartUtc, DateTime horizonEndUtc)
    {
        var issues = Validate(horizonStartUtc, horizonEndUtc);
        if (issues.Count > 0)
            throw new InvalidOperationException(string.Join(" ", issues));

        var objectiveWeights = new CampaignObjectiveWeights(
            ServiceRiskPerMtDay,
            EarlyProductionPerMtDay,
            CampaignSetupCost,
            ResidualHeatPerMt,
            BelowMinimumCampaignPerMt)
        {
            GradeTransitionCostWeight = GradeTransitionCostWeight,
            HeatTargetDeviationPerMt = HeatTargetDeviationPerMt,
            CampaignStabilityChangePerMt = CampaignStabilityChangePerMt
        };

        var campaignPolicy = new CampaignPlanningPolicy(
            NominalHeatSizeMt,
            MinimumHeatSizeMt,
            MaximumHeatSizeMt,
            TargetCampaignQuantityMt,
            MaximumCampaignQuantityMt,
            AllowMtoMtsMixing,
            AllowMixedGradesWithinSequenceClass,
            ExpectedCastingYieldPct,
            objectiveWeights);

        var structurePolicy = new ProductionStructurePlanningPolicy(
            MaximumHeatsPerCastSequence,
            DefaultCastingMinutesPerHeat,
            SequenceBreakPenalty,
            CastingYieldPct,
            DefaultRollingMinutesPer100Mt,
            AllowCrossCampaignCastSequences,
            AllowCrossCampaignRollingPlans);

        return new PlanningCalculationRequest(
            new PlanningDemandSelection(),
            campaignPolicy,
            structurePolicy,
            horizonStartUtc,
            horizonEndUtc,
            MaxSolverSeconds,
            AssignmentPolicies: BuildAssignmentPolicies(),
            ScenarioCode: string.IsNullOrWhiteSpace(ScenarioCode) ? null : ScenarioCode.Trim());
    }

    public PlanningTimeFencePolicy BuildTimeFencePolicy() => new(
        FrozenMinutes,
        SlushyMinutes,
        SlushyMovementPenaltyPerMinute,
        SlushyResourceChangePenalty);

    public RepairScopePolicy BuildRepairScopePolicy() => new(
        RepairSuccessorDepth,
        RepairHorizonMinutes,
        FreezeUnaffectedOperations,
        IncludeSameResourceNeighbors);

    public IReadOnlyList<string> Validate(DateTime? horizonStartUtc = null, DateTime? horizonEndUtc = null)
    {
        var issues = new List<string>();
        if (horizonStartUtc.HasValue && horizonEndUtc.HasValue && horizonEndUtc <= horizonStartUtc)
            issues.Add("Horizon end must be after horizon start.");
        if (MinimumHeatSizeMt <= 0m) issues.Add("Minimum heat quantity must be greater than zero.");
        if (NominalHeatSizeMt < MinimumHeatSizeMt) issues.Add("Nominal heat quantity must be at least the minimum heat quantity.");
        if (MaximumHeatSizeMt < NominalHeatSizeMt) issues.Add("Maximum heat quantity must be at least the nominal heat quantity.");
        if (TargetCampaignQuantityMt <= 0m) issues.Add("Target campaign quantity must be greater than zero.");
        if (MaximumCampaignQuantityMt < MaximumHeatSizeMt) issues.Add("Maximum campaign quantity must be at least one maximum-size heat.");
        if (TargetCampaignQuantityMt > MaximumCampaignQuantityMt) issues.Add("Target campaign quantity cannot exceed maximum campaign quantity.");
        if (ExpectedCastingYieldPct <= 0m || ExpectedCastingYieldPct > 100m) issues.Add("Expected casting yield must be greater than 0 and at most 100 percent.");
        if (CastingYieldPct <= 0m || CastingYieldPct > 100m) issues.Add("Structure casting yield must be greater than 0 and at most 100 percent.");
        if (MaximumHeatsPerCastSequence <= 0) issues.Add("Maximum heats per cast sequence must be greater than zero.");
        if (DefaultCastingMinutesPerHeat <= 0) issues.Add("Default casting minutes per heat must be greater than zero.");
        if (DefaultRollingMinutesPer100Mt <= 0) issues.Add("Default rolling minutes per 100 MT must be greater than zero.");
        if (SequenceBreakPenalty < 0) issues.Add("Sequence break penalty cannot be negative.");
        if (ServiceRiskPerMtDay < 0m || EarlyProductionPerMtDay < 0m || CampaignSetupCost < 0m ||
            ResidualHeatPerMt < 0m || BelowMinimumCampaignPerMt < 0m || GradeTransitionCostWeight < 0m ||
            HeatTargetDeviationPerMt < 0m || CampaignStabilityChangePerMt < 0m)
            issues.Add("Objective weights cannot be negative.");
        if (FrozenMinutes < 0 || SlushyMinutes < 0) issues.Add("Time-fence durations cannot be negative.");
        if (SlushyMovementPenaltyPerMinute < 0 || SlushyResourceChangePenalty < 0) issues.Add("Time-fence penalties cannot be negative.");
        if (MaxSolverSeconds <= 0) issues.Add("Solver time must be greater than zero seconds.");
        if (UseAssignmentCommitmentPolicy)
        {
            if (FirmMinutesBeforeStart < 0 || CommitMinutesBeforeStart < 0)
                issues.Add("Commitment windows cannot be negative.");
            if (CommitMinutesBeforeStart > FirmMinutesBeforeStart)
                issues.Add("Commit window must be inside the firm window.");
        }
        if (RepairSuccessorDepth < 0 || RepairHorizonMinutes <= 0)
            issues.Add("Repair scope must use a non-negative successor depth and a positive repair horizon.");
        return issues;
    }

    public void ApplyAssumptions(PlanningAssumptions? assumptions)
    {
        if (assumptions is null) return;
        ScenarioCode = assumptions.ScenarioCode;

        var weights = assumptions.CampaignObjectiveWeights;
        ServiceRiskPerMtDay = weights.ServiceRiskPerMtDay;
        EarlyProductionPerMtDay = weights.EarlyProductionPerMtDay;
        CampaignSetupCost = weights.CampaignSetupCost;
        ResidualHeatPerMt = weights.ResidualHeatPerMt;
        BelowMinimumCampaignPerMt = weights.BelowMinimumCampaignPerMt;
        GradeTransitionCostWeight = weights.GradeTransitionCostWeight;
        HeatTargetDeviationPerMt = weights.HeatTargetDeviationPerMt;
        CampaignStabilityChangePerMt = weights.CampaignStabilityChangePerMt;

        if (assumptions.CampaignPolicy is { } campaign)
        {
            NominalHeatSizeMt = campaign.NominalHeatSizeMt;
            MinimumHeatSizeMt = campaign.MinimumHeatSizeMt;
            MaximumHeatSizeMt = campaign.MaximumHeatSizeMt;
            TargetCampaignQuantityMt = campaign.TargetCampaignQuantityMt;
            MaximumCampaignQuantityMt = campaign.MaximumCampaignQuantityMt;
            AllowMtoMtsMixing = campaign.AllowMtoMtsMixing;
            AllowMixedGradesWithinSequenceClass = campaign.AllowMixedGradesWithinSequenceClass;
            ExpectedCastingYieldPct = campaign.ExpectedCastingYieldPct;
        }

        if (assumptions.StructurePolicy is { } structure)
        {
            MaximumHeatsPerCastSequence = structure.MaximumHeatsPerCastSequence;
            DefaultCastingMinutesPerHeat = structure.DefaultCastingMinutesPerHeat;
            SequenceBreakPenalty = structure.SequenceBreakPenalty;
            CastingYieldPct = structure.CastingYieldPct;
            DefaultRollingMinutesPer100Mt = structure.DefaultRollingMinutesPer100Mt;
            AllowCrossCampaignCastSequences = structure.AllowCrossCampaignCastSequences;
            AllowCrossCampaignRollingPlans = structure.AllowCrossCampaignRollingPlans;
        }

        if (assumptions.TimeFencePolicy is { } timeFence)
        {
            FrozenMinutes = timeFence.FrozenMinutes;
            SlushyMinutes = timeFence.SlushyMinutes;
            SlushyMovementPenaltyPerMinute = timeFence.SlushyMovementPenaltyPerMinute;
            SlushyResourceChangePenalty = timeFence.SlushyResourceChangePenalty;
        }

        if (assumptions.AssignmentPolicies is { Count: > 0 } assignmentPolicies)
        {
            var first = assignmentPolicies.First();
            UseAssignmentCommitmentPolicy = true;
            FirmMinutesBeforeStart = first.FirmMinutesBeforeStart;
            CommitMinutesBeforeStart = first.CommitMinutesBeforeStart;
            AllowRedispatchWhenFirm = first.AllowRedispatchWhenFirm;
            AllowRedispatchWhenCommittedForDisruption = first.AllowRedispatchWhenCommittedForDisruption;
            CommitWhenPredecessorRunning = first.CommitWhenPredecessorRunning;
            CommitWhenPredecessorCompleted = first.CommitWhenPredecessorCompleted;
            RequireDispatchAcknowledgement = first.RequireDispatchAcknowledgement;
        }
        else
        {
            UseAssignmentCommitmentPolicy = false;
        }

        if (assumptions.MaxSolverSeconds is { } solverSeconds)
            MaxSolverSeconds = solverSeconds;
    }

    public void Reset()
    {
        var defaults = new PlannerConstraintState();
        NominalHeatSizeMt = defaults.NominalHeatSizeMt;
        MinimumHeatSizeMt = defaults.MinimumHeatSizeMt;
        MaximumHeatSizeMt = defaults.MaximumHeatSizeMt;
        TargetCampaignQuantityMt = defaults.TargetCampaignQuantityMt;
        MaximumCampaignQuantityMt = defaults.MaximumCampaignQuantityMt;
        AllowMtoMtsMixing = defaults.AllowMtoMtsMixing;
        AllowMixedGradesWithinSequenceClass = defaults.AllowMixedGradesWithinSequenceClass;
        ExpectedCastingYieldPct = defaults.ExpectedCastingYieldPct;
        ServiceRiskPerMtDay = defaults.ServiceRiskPerMtDay;
        EarlyProductionPerMtDay = defaults.EarlyProductionPerMtDay;
        CampaignSetupCost = defaults.CampaignSetupCost;
        ResidualHeatPerMt = defaults.ResidualHeatPerMt;
        BelowMinimumCampaignPerMt = defaults.BelowMinimumCampaignPerMt;
        GradeTransitionCostWeight = defaults.GradeTransitionCostWeight;
        HeatTargetDeviationPerMt = defaults.HeatTargetDeviationPerMt;
        CampaignStabilityChangePerMt = defaults.CampaignStabilityChangePerMt;
        MaximumHeatsPerCastSequence = defaults.MaximumHeatsPerCastSequence;
        DefaultCastingMinutesPerHeat = defaults.DefaultCastingMinutesPerHeat;
        SequenceBreakPenalty = defaults.SequenceBreakPenalty;
        CastingYieldPct = defaults.CastingYieldPct;
        DefaultRollingMinutesPer100Mt = defaults.DefaultRollingMinutesPer100Mt;
        AllowCrossCampaignCastSequences = defaults.AllowCrossCampaignCastSequences;
        AllowCrossCampaignRollingPlans = defaults.AllowCrossCampaignRollingPlans;
        FrozenMinutes = defaults.FrozenMinutes;
        SlushyMinutes = defaults.SlushyMinutes;
        SlushyMovementPenaltyPerMinute = defaults.SlushyMovementPenaltyPerMinute;
        SlushyResourceChangePenalty = defaults.SlushyResourceChangePenalty;
        MaxSolverSeconds = defaults.MaxSolverSeconds;
        ScenarioCode = null;
        UseAssignmentCommitmentPolicy = defaults.UseAssignmentCommitmentPolicy;
        FirmMinutesBeforeStart = defaults.FirmMinutesBeforeStart;
        CommitMinutesBeforeStart = defaults.CommitMinutesBeforeStart;
        AllowRedispatchWhenFirm = defaults.AllowRedispatchWhenFirm;
        AllowRedispatchWhenCommittedForDisruption = defaults.AllowRedispatchWhenCommittedForDisruption;
        CommitWhenPredecessorRunning = defaults.CommitWhenPredecessorRunning;
        CommitWhenPredecessorCompleted = defaults.CommitWhenPredecessorCompleted;
        RequireDispatchAcknowledgement = defaults.RequireDispatchAcknowledgement;
        RepairSuccessorDepth = defaults.RepairSuccessorDepth;
        RepairHorizonMinutes = defaults.RepairHorizonMinutes;
        FreezeUnaffectedOperations = defaults.FreezeUnaffectedOperations;
        IncludeSameResourceNeighbors = defaults.IncludeSameResourceNeighbors;
    }

    private IReadOnlyCollection<OperationAssignmentPolicy>? BuildAssignmentPolicies()
    {
        if (!UseAssignmentCommitmentPolicy) return null;
        return Enum.GetValues<ProcessOperationType>()
            .Where(x => x != ProcessOperationType.Unknown)
            .Select(x => new OperationAssignmentPolicy(
                x,
                FirmMinutesBeforeStart,
                CommitMinutesBeforeStart,
                AllowRedispatchWhenFirm,
                AllowRedispatchWhenCommittedForDisruption,
                CommitWhenPredecessorRunning,
                CommitWhenPredecessorCompleted,
                RequireDispatchAcknowledgement))
            .ToArray();
    }
}
