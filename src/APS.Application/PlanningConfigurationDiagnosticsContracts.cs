namespace APS.Application;

/// <summary>
/// Planner-facing severity for configuration diagnostics. Blockers are expected to stop a canonical
/// Calculate/Replan for at least one current manufacturing requirement; warnings can still produce a
/// plan but make planning less explicit or robust.
/// </summary>
public enum PlanningConfigurationDiagnosticSeverity
{
    Information = 1,
    Warning = 2,
    Blocker = 3
}

public sealed record PlanningConfigurationDiagnostic(
    PlanningConfigurationDiagnosticSeverity Severity,
    string Code,
    string Area,
    string Title,
    string Message,
    string? EntityCode = null,
    Guid? EntityId = null,
    string? FixHref = null,
    string? FixLabel = null);

public sealed record PlanningConfigurationDiagnosticsView(
    DateTime GeneratedOnUtc,
    int ProductionOrderCount,
    int RouteCount,
    int ResourceCount,
    IReadOnlyCollection<PlanningConfigurationDiagnostic> Diagnostics)
{
    public int BlockerCount => Diagnostics.Count(x => x.Severity == PlanningConfigurationDiagnosticSeverity.Blocker);
    public int WarningCount => Diagnostics.Count(x => x.Severity == PlanningConfigurationDiagnosticSeverity.Warning);
    public bool IsPlanningReady => BlockerCount == 0;
}

/// <summary>
/// Read-only preflight over current persisted demand/manufacturing requirements and authoritative
/// technical master data. It diagnoses; it never mutates master data to make a demo plan pass.
/// </summary>
public interface IPlanningConfigurationDiagnosticsService
{
    Task<PlanningConfigurationDiagnosticsView> GetAsync(CancellationToken cancellationToken = default);
}
