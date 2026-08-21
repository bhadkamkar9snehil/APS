namespace APS.Domain;

public enum BilletThermalSourceBasis
{
    PlannedCcm = 1,
    ActualMeasurement = 2,
    CategoricalCommitted = 3,
    CategoricalExternal = 4,
    UnknownYard = 5,
    Reheated = 6
}

public enum BilletThermalOutcome
{
    HotDirect = 1,
    HotBuffered = 2,
    ReheatingRequired = 3,
    Reheated = 4,
    Infeasible = 5
}
