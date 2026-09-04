namespace APS.UI.State;

public sealed record ResourceCalendarDraftRequest(
    Guid ResourceId,
    DateTime StartUtc,
    DateTime EndUtc,
    bool IsAvailable,
    decimal? CapacityFactorPct,
    string ReasonCode);
