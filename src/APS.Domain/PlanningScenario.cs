namespace APS.Domain;

public sealed class PlanningScenario : Entity
{
    public required string ScenarioCode { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsBaseline { get; set; }
    public DateTime CreatedOnUtc { get; set; } = DateTime.UtcNow;
    public ICollection<ResourceScenarioOverride> ResourceOverrides { get; set; } = new List<ResourceScenarioOverride>();
}

public sealed class ResourceScenarioOverride : Entity
{
    public Guid PlanningScenarioId { get; set; }
    public PlanningScenario? PlanningScenario { get; set; }
    public Guid ResourceId { get; set; }
    public ResourceOperatingState OperatingState { get; set; }
    public decimal? CapacityFactorPct { get; set; }
    public DateTime? EffectiveFromUtc { get; set; }
    public DateTime? EffectiveToUtc { get; set; }
    public ProcessOperationType? RestrictedProcessOperationType { get; set; }
    public string? AllowedGradeCode { get; set; }
    public string? ForbiddenGradeCode { get; set; }
    public string? Reason { get; set; }
}
