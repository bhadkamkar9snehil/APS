using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace APS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillsOfMaterial",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BomCode = table.Column<string>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EffectiveToUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OutputMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputMaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    OutputQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    OutputUom = table.Column<string>(type: "TEXT", nullable: false),
                    PlantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    SelectionPriority = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillsOfMaterial", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Campaigns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignNumber = table.Column<string>(type: "TEXT", nullable: false),
                    GradeSequenceClassCode = table.Column<string>(type: "TEXT", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelRequirementMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Campaigns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CastSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CasterResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    TundishNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PlannedStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CastSequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrossSectionSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    Shape = table.Column<int>(type: "INTEGER", nullable: false),
                    WidthMm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    HeightMm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ThicknessMm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    DiameterMm = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    SectionFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CasterFormatClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    RollingFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    TheoreticalKgPerM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrossSectionSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalMaterialSupplies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplyReference = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierCode = table.Column<string>(type: "TEXT", nullable: true),
                    CertificateReference = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ReservedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AvailableFromUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    QualityStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ThermalState = table.Column<int>(type: "INTEGER", nullable: true),
                    EstimatedTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    IsFirm = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsagePenalty = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalMaterialSupplies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeatExecutionActuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalHeatNumber = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalCastNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CasterResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ChangedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalEventId = table.Column<string>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeatExecutionActuals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LotGenealogy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentLotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChildLotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    TransformationWorkOrderId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LotGenealogy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingRoutes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingRoutes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialLots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LotNumber = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    ProductForm = table.Column<int>(type: "INTEGER", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    QualityStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplySourceType = table.Column<int>(type: "INTEGER", nullable: true),
                    SupplierCode = table.Column<string>(type: "TEXT", nullable: true),
                    CertificateReference = table.Column<string>(type: "TEXT", nullable: true),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProducedByWorkOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    HeatNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CastNumber = table.Column<string>(type: "TEXT", nullable: true),
                    StrandNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ProducedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AvailableFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ThermalState = table.Column<int>(type: "INTEGER", nullable: true),
                    EstimatedTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialSourcingRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RuleCode = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProductForm = table.Column<int>(type: "INTEGER", nullable: true),
                    DestinationLocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    AllowMake = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowBuy = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowTransfer = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowManualSupply = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreferredAction = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseLeadTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    TransferLeadTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    PreferredSupplierCode = table.Column<string>(type: "TEXT", nullable: true),
                    TransferSourceLocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumBuyQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    BuyOrderMultipleMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumTransferQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MakePenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    BuyPenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    TransferPenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialSourcingRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: false),
                    SapMaterialCode = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductForm = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    RouteFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    StandardCutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    UnitWeightKg = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ExpectedYieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TmtApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackagingSpecifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackagingCode = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: false),
                    PackagingUnitType = table.Column<int>(type: "INTEGER", nullable: false),
                    StandardCutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetUnitWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumUnitWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumUnitWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetPiecesPerUnit = table.Column<int>(type: "INTEGER", nullable: true),
                    AllowMixedHeats = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowMixedLots = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowRemainderUnit = table.Column<bool>(type: "INTEGER", nullable: false),
                    MarkingRequirementCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackagingSpecifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlannedPackagingUnits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PackagingUnitType = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PlannedPieceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PackagingCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedIdentifier = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlannedPackagingUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlantAreas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantAreas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlantFlowLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ToResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FromProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    ToProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    CouplingType = table.Column<int>(type: "INTEGER", nullable: false),
                    MinimumTransferTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    MaximumTransferTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    SupportsHotTransfer = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInventoryDecouplingPoint = table.Column<bool>(type: "INTEGER", nullable: false),
                    NominalTemperatureLossCPerMinute = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlantFlowLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Plants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Plants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: true),
                    IsReleased = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCalendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    End = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CapacityFactorPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCalendars", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CastingClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ThroughputMtPerHour = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    FixedDurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignmentPenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceCapabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessStageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ResourceType = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessUnitType = table.Column<int>(type: "INTEGER", nullable: false),
                    OperatingState = table.Column<int>(type: "INTEGER", nullable: false),
                    CapacityFactorPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinimumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NominalHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LadleCapacityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    WorkingCapacityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NominalThroughputMtPerHour = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumResidenceMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    NominalResidenceMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumResidenceMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    StrandCount = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumHeatsPerSequence = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumHeatsPerTundish = table.Column<int>(type: "INTEGER", nullable: true),
                    MinimumCastingSpeedMPerMin = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    NominalCastingSpeedMPerMin = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCastingSpeedMPerMin = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ExpectedYieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    SupportsHotCharge = table.Column<bool>(type: "INTEGER", nullable: false),
                    SupportsColdCharge = table.Column<bool>(type: "INTEGER", nullable: false),
                    TargetDischargeTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RollingPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RollingMillResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollingPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RouteResourceCapabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    CastingClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputSectionFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputSectionFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputCasterFormatClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputRollingFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ThroughputMtPerHour = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    FixedDurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    AssignmentPenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPreferred = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteResourceCapabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    ItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    FinalCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    OrderQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    OpenQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CustomerCode = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalStatus = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: true),
                    Start = table.Column<DateTime>(type: "TEXT", nullable: false),
                    End = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsFrozen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SteelGrades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    SequenceClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    CastingClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    QualityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultCasterSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    DefaultRouteCode = table.Column<string>(type: "TEXT", nullable: true),
                    LiquidusTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    HotChargeEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    ColdChargeEligible = table.Column<bool>(type: "INTEGER", nullable: false),
                    TmtApplicable = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SteelGrades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TransitionRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceType = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessUnitType = table.Column<int>(type: "INTEGER", nullable: true),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: true),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    Dimension = table.Column<int>(type: "INTEGER", nullable: false),
                    FromCode = table.Column<string>(type: "TEXT", nullable: false),
                    ToCode = table.Column<string>(type: "TEXT", nullable: false),
                    IsAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresSequenceBreak = table.Column<bool>(type: "INTEGER", nullable: false),
                    Penalty = table.Column<int>(type: "INTEGER", nullable: false),
                    TransitionTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransitionRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    WorkOrderType = table.Column<int>(type: "INTEGER", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ActualQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalExecutionId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillOfMaterialComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BillOfMaterialId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ComponentMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    ComponentMaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    ComponentGradeCode = table.Column<string>(type: "TEXT", nullable: true),
                    ComponentCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    FlowType = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityPerOutput = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Uom = table.Column<string>(type: "TEXT", nullable: false),
                    YieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ScrapPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    LossPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    RequiredAtOffsetMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    QualityClassCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillOfMaterialComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillOfMaterialComponents_BillsOfMaterial_BillOfMaterialId",
                        column: x => x.BillOfMaterialId,
                        principalTable: "BillsOfMaterial",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignGradeSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignGradeSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignGradeSequences_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StrandMaterialActuals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HeatExecutionActualId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrandNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalLotNumber = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ProducedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StrandMaterialActuals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StrandMaterialActuals_HeatExecutionActuals_HeatExecutionActualId",
                        column: x => x.HeatExecutionActualId,
                        principalTable: "HeatExecutionActuals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ManufacturingRouteOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ManufacturingRouteId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseWorkOrderType = table.Column<int>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: true),
                    IsInventoryDecouplingPoint = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiresHotMaterial = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredChargeMode = table.Column<int>(type: "INTEGER", nullable: true),
                    MinimumQueueTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    MaximumQueueTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    YieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManufacturingRouteOperations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ManufacturingRouteOperations_ManufacturingRoutes_ManufacturingRouteId",
                        column: x => x.ManufacturingRouteId,
                        principalTable: "ManufacturingRoutes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlantAreaId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessStages_PlantAreas_PlantAreaId",
                        column: x => x.PlantAreaId,
                        principalTable: "PlantAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperationDispatchRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: false),
                    PreviousResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisedResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ChangedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true),
                    Source = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperationDispatchRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OperationDispatchRevisions_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCampaignAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCampaignAllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCampaignAllocationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCampaignGradeSequenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCampaignGradeSequenceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCampaignGradeSequenceSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCampaignSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignNumber = table.Column<string>(type: "TEXT", nullable: false),
                    GradeSequenceClassCode = table.Column<string>(type: "TEXT", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelRequirementMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCampaignSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCampaignSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCastSequenceHeatSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CastSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCastSequenceHeatSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCastSequenceHeatSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanCastSequenceSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CastSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CasterResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    TundishNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    PlannedStart = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlannedEnd = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanCastSequenceSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanCastSequenceSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanDemandCoverageSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QualityStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDemandCoverageSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanDemandCoverageSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanDemandSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SalesOrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    SalesOrderItemNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CustomerCode = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    FinalCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    OpenDemandQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FinishedGoodsCoveredQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ManufacturingRequirementQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CustomerRequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConfirmedDeliveryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionRequiredByDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannerAttentionRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanDemandSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanDemandSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanHeatAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedOutputQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PlannedInputQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanHeatAllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanHeatAllocationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanHeatSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignGradeSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinimumFeasibleQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumFeasibleQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PreferredSteelmakingResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreferredCasterResourceId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanHeatSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanHeatSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanInventoryAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    UseCode = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanInventoryAllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanInventoryAllocationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanMaterialUnitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CastSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CasterResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StrandNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    UnitSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    AvailableOnUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanMaterialUnitSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanMaterialUnitSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanOperationResourceOptionSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignmentPenalty = table.Column<int>(type: "INTEGER", nullable: false),
                    WasSelected = table.Column<bool>(type: "INTEGER", nullable: false),
                    EligibilityBasisCode = table.Column<string>(type: "TEXT", nullable: true),
                    CapturedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanOperationResourceOptionSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanOperationResourceOptionSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanOperationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanningKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceEntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommittedResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ActualResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AssignmentCommitmentState = table.Column<int>(type: "INTEGER", nullable: false),
                    EligibleResourceOptionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    PredecessorPlanningKeysJson = table.Column<string>(type: "TEXT", nullable: true),
                    AssignmentPolicyJson = table.Column<string>(type: "TEXT", nullable: true),
                    CommitmentLastEvaluatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreviousPlannedResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RedispatchReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    RedispatchComment = table.Column<string>(type: "TEXT", nullable: true),
                    RedispatchedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExecutionStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ActualStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualEndUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ActualQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    LastExecutionChangedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ExecutionHistoryJson = table.Column<string>(type: "TEXT", nullable: true),
                    IsOffPlanActualResource = table.Column<bool>(type: "INTEGER", nullable: false),
                    OffPlanActualReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanOperationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanOperationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanOrderRequirementSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SalesOrderItem = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerCode = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeSequenceClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    CastingClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    QualityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    FinalCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    SegregationPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    VdRequirement = table.Column<int>(type: "INTEGER", nullable: false),
                    ReheatRequirement = table.Column<int>(type: "INTEGER", nullable: false),
                    TmtRequirement = table.Column<int>(type: "INTEGER", nullable: false),
                    HotChargeAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinimumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    AllowMixedHeatBundle = table.Column<bool>(type: "INTEGER", nullable: true),
                    MarkingRequirementCode = table.Column<string>(type: "TEXT", nullable: true),
                    InspectionRequirementCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequirementReference = table.Column<string>(type: "TEXT", nullable: true),
                    RequirementFingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanOrderRequirementSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanOrderRequirementSnapshot_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanPackagingUnitSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedPackagingUnitId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PackagingUnitType = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PlannedPieceCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PackagingCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedIdentifier = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanPackagingUnitSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanPackagingUnitSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanRollingPlanAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RollingPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRollingPlanAllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRollingPlanAllocationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanRollingPlanSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RollingPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RollingMillResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRollingPlanSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRollingPlanSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanRouteOperationAllocationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteOperationPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRouteOperationAllocationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRouteOperationAllocationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanRouteOperationSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteOperationPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    UpstreamPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    ReleaseWorkOrderType = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    InputMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    OutputMaterialSpecificationCode = table.Column<string>(type: "TEXT", nullable: true),
                    InputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    OutputCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinimumQueueTime = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    MaximumQueueTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    IsInventoryDecouplingPoint = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRouteOperationSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRouteOperationSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanVersionStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ParentPlanVersionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Trigger = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceTimeUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HorizonStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    HorizonEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SolverStatus = table.Column<string>(type: "TEXT", nullable: true),
                    ObjectiveValue = table.Column<long>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaterialRequirementsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSupplyRequirementsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialReservationsJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialLedgerJson = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialSourcingAlternativesJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanVersionStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanVersionStates_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderRequirementProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QualityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    SegregationPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireVd = table.Column<bool>(type: "INTEGER", nullable: true),
                    ForbidVd = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequireReheating = table.Column<bool>(type: "INTEGER", nullable: true),
                    ForbidHotCharge = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequireTmt = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequiredRouteCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequiredResourceGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    AllowMixedHeatBundle = table.Column<bool>(type: "INTEGER", nullable: true),
                    MarkingRequirementCode = table.Column<string>(type: "TEXT", nullable: true),
                    InspectionRequirementCode = table.Column<string>(type: "TEXT", nullable: true),
                    QualificationFingerprint = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderRequirementProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderRequirementProfiles_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeChemistryRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SteelGradeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeChemistryRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeChemistryRequirements_SteelGrades_SteelGradeId",
                        column: x => x.SteelGradeId,
                        principalTable: "SteelGrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GradeProcessRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SteelGradeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumProcessMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumProcessMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumQueueMinutesAfterOperation = table.Column<int>(type: "INTEGER", nullable: true),
                    MinimumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ExpectedYieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradeProcessRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradeProcessRequirements_SteelGrades_SteelGradeId",
                        column: x => x.SteelGradeId,
                        principalTable: "SteelGrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    DemandSource = table.Column<int>(type: "INTEGER", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    SteelGradeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeSequenceClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    FinalCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RemainingQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetStockMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ProjectedAvailableStockMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    StockPolicyCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductionOrders_SteelGrades_SteelGradeId",
                        column: x => x.SteelGradeId,
                        principalTable: "SteelGrades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderStatusHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PreviousStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    NewStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    ExternalEventId = table.Column<string>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderStatusHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderStatusHistory_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignHeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignGradeSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    MinimumFeasibleQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumFeasibleQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    PreferredSteelmakingResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PreferredCasterResourceId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignHeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignHeats_CampaignGradeSequences_CampaignGradeSequenceId",
                        column: x => x.CampaignGradeSequenceId,
                        principalTable: "CampaignGradeSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CampaignHeats_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanChemistryRequirementSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanOrderRequirementSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanChemistryRequirementSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanChemistryRequirementSnapshot_PlanOrderRequirementSnapshot_PlanOrderRequirementSnapshotId",
                        column: x => x.PlanOrderRequirementSnapshotId,
                        principalTable: "PlanOrderRequirementSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanProcessRequirementSnapshot",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanOrderRequirementSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MinimumProcessMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumProcessMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MaximumQueueMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    MinimumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumHeatWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ExpectedYieldPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanProcessRequirementSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanProcessRequirementSnapshot_PlanOrderRequirementSnapshot_PlanOrderRequirementSnapshotId",
                        column: x => x.PlanOrderRequirementSnapshotId,
                        principalTable: "PlanOrderRequirementSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanProductionOrderSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlanVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderNumber = table.Column<string>(type: "TEXT", nullable: false),
                    DemandSource = table.Column<int>(type: "INTEGER", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SalesOrderNumber = table.Column<string>(type: "TEXT", nullable: true),
                    SalesOrderItemNumber = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerCode = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    GradeSequenceClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    FinalCrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    CasterSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    RouteCode = table.Column<string>(type: "TEXT", nullable: false),
                    ProductFamilyCode = table.Column<string>(type: "TEXT", nullable: true),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RemainingQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TargetStockMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    ProjectedAvailableStockMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    StockPolicyCode = table.Column<string>(type: "TEXT", nullable: true),
                    FinishedGoodsAllocatedMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RollingRequirementMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateAllocatedMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExternalIntermediateAllocatedMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelRequirementMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    RequirementSnapshotId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanProductionOrderSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanProductionOrderSnapshots_PlanOrderRequirementSnapshot_RequirementSnapshotId",
                        column: x => x.RequirementSnapshotId,
                        principalTable: "PlanOrderRequirementSnapshot",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PlanProductionOrderSnapshots_PlanVersions_PlanVersionId",
                        column: x => x.PlanVersionId,
                        principalTable: "PlanVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderChemistryRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderRequirementProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderChemistryRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderChemistryRequirements_SalesOrderRequirementProfiles_SalesOrderRequirementProfileId",
                        column: x => x.SalesOrderRequirementProfileId,
                        principalTable: "SalesOrderRequirementProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderProcessRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderRequirementProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaximumQueueMinutes = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderProcessRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderProcessRequirements_SalesOrderRequirementProfiles_SalesOrderRequirementProfileId",
                        column: x => x.SalesOrderRequirementProfileId,
                        principalTable: "SalesOrderRequirementProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignAllocations_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignAllocations_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MaterialLotAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MaterialLotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AllocatedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialLotAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialLotAllocations_MaterialLots_MaterialLotId",
                        column: x => x.MaterialLotId,
                        principalTable: "MaterialLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MaterialLotAllocations_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductionOrderRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerCode = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequirementReference = table.Column<string>(type: "TEXT", nullable: true),
                    QualityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    SegregationPolicy = table.Column<int>(type: "INTEGER", nullable: false),
                    RequireVd = table.Column<bool>(type: "INTEGER", nullable: true),
                    ForbidVd = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequireReheating = table.Column<bool>(type: "INTEGER", nullable: true),
                    ForbidHotCharge = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequireTmt = table.Column<bool>(type: "INTEGER", nullable: true),
                    RequiredRouteCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequiredResourceGroupCode = table.Column<string>(type: "TEXT", nullable: true),
                    MinimumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumSuperheatC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCastingTemperatureC = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    CutLengthM = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumBundleWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MinimumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumCoilWeightMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    AllowMixedHeatBundle = table.Column<bool>(type: "INTEGER", nullable: true),
                    MarkingRequirementCode = table.Column<string>(type: "TEXT", nullable: true),
                    InspectionRequirementCode = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductionOrderRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductionOrderRequirements_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RollingPlanAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RollingPlanId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ExistingIntermediateInventoryMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FreshSteelQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RollingPlanAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RollingPlanAllocations_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RollingPlanAllocations_RollingPlans_RollingPlanId",
                        column: x => x.RollingPlanId,
                        principalTable: "RollingPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderDemandStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OpenDemandQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    FinishedGoodsCoveredQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    ManufacturingRequirementQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    CustomerRequiredDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConfirmedDeliveryDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProductionRequiredByDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Disposition = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannerAttentionRequired = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReasonCode = table.Column<string>(type: "TEXT", nullable: true),
                    CalculatedOnUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderDemandStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderDemandStates_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SalesOrderDemandStates_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkOrderAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkOrderAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkOrderAllocations_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkOrderAllocations_WorkOrders_WorkOrderId",
                        column: x => x.WorkOrderId,
                        principalTable: "WorkOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CampaignHeatAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlannedOutputQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false),
                    PlannedInputQuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignHeatAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignHeatAllocations_CampaignHeats_CampaignHeatId",
                        column: x => x.CampaignHeatId,
                        principalTable: "CampaignHeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CampaignHeatAllocations_ProductionOrders_ProductionOrderId",
                        column: x => x.ProductionOrderId,
                        principalTable: "ProductionOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CastSequenceHeats",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CastSequenceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignHeatId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CastSequenceHeats", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CastSequenceHeats_CampaignHeats_CampaignHeatId",
                        column: x => x.CampaignHeatId,
                        principalTable: "CampaignHeats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CastSequenceHeats_CastSequences_CastSequenceId",
                        column: x => x.CastSequenceId,
                        principalTable: "CastSequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderChemistryRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderRequirementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ElementCode = table.Column<string>(type: "TEXT", nullable: false),
                    MinimumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    TargetPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true),
                    MaximumPct = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderChemistryRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderChemistryRequirements_ProductionOrderRequirements_ProductionOrderRequirementId",
                        column: x => x.ProductionOrderRequirementId,
                        principalTable: "ProductionOrderRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderProcessRequirements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProductionOrderRequirementId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProcessOperationType = table.Column<int>(type: "INTEGER", nullable: false),
                    Requirement = table.Column<int>(type: "INTEGER", nullable: false),
                    CapabilityClassCode = table.Column<string>(type: "TEXT", nullable: true),
                    RequiredResourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaximumQueueMinutes = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderProcessRequirements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderProcessRequirements_ProductionOrderRequirements_ProductionOrderRequirementId",
                        column: x => x.ProductionOrderRequirementId,
                        principalTable: "ProductionOrderRequirements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderFinishedGoodsCoverage",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SalesOrderDemandStateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MaterialCode = table.Column<string>(type: "TEXT", nullable: false),
                    GradeCode = table.Column<string>(type: "TEXT", nullable: false),
                    CrossSectionCode = table.Column<string>(type: "TEXT", nullable: false),
                    LocationCode = table.Column<string>(type: "TEXT", nullable: true),
                    AvailableFromUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    QualityStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    QuantityMt = table.Column<decimal>(type: "TEXT", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderFinishedGoodsCoverage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderFinishedGoodsCoverage_SalesOrderDemandStates_SalesOrderDemandStateId",
                        column: x => x.SalesOrderDemandStateId,
                        principalTable: "SalesOrderDemandStates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillOfMaterialComponents_BillOfMaterialId_SequenceNumber",
                table: "BillOfMaterialComponents",
                columns: new[] { "BillOfMaterialId", "SequenceNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfMaterial_BomCode_VersionNumber",
                table: "BillsOfMaterial",
                columns: new[] { "BomCode", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillsOfMaterial_OutputMaterialCode_Status_EffectiveFromUtc",
                table: "BillsOfMaterial",
                columns: new[] { "OutputMaterialCode", "Status", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAllocations_CampaignId",
                table: "CampaignAllocations",
                column: "CampaignId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignAllocations_ProductionOrderId",
                table: "CampaignAllocations",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignGradeSequences_CampaignId_SequenceNumber",
                table: "CampaignGradeSequences",
                columns: new[] { "CampaignId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CampaignHeatAllocations_CampaignHeatId_ProductionOrderId",
                table: "CampaignHeatAllocations",
                columns: new[] { "CampaignHeatId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignHeatAllocations_ProductionOrderId",
                table: "CampaignHeatAllocations",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignHeats_CampaignGradeSequenceId",
                table: "CampaignHeats",
                column: "CampaignGradeSequenceId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignHeats_CampaignId_SequenceNumber",
                table: "CampaignHeats",
                columns: new[] { "CampaignId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Campaigns_CampaignNumber",
                table: "Campaigns",
                column: "CampaignNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CastSequenceHeats_CampaignHeatId",
                table: "CastSequenceHeats",
                column: "CampaignHeatId");

            migrationBuilder.CreateIndex(
                name: "IX_CastSequenceHeats_CastSequenceId_Position",
                table: "CastSequenceHeats",
                columns: new[] { "CastSequenceId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrossSectionSpecifications_CrossSectionCode",
                table: "CrossSectionSpecifications",
                column: "CrossSectionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalMaterialSupplies_SourceType_SupplyReference",
                table: "ExternalMaterialSupplies",
                columns: new[] { "SourceType", "SupplyReference" });

            migrationBuilder.CreateIndex(
                name: "IX_GradeChemistryRequirements_SteelGradeId_ElementCode",
                table: "GradeChemistryRequirements",
                columns: new[] { "SteelGradeId", "ElementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GradeProcessRequirements_SteelGradeId_ProcessOperationType",
                table: "GradeProcessRequirements",
                columns: new[] { "SteelGradeId", "ProcessOperationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HeatExecutionActuals_PlanVersionId_PlanningKey_ChangedOnUtc",
                table: "HeatExecutionActuals",
                columns: new[] { "PlanVersionId", "PlanningKey", "ChangedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HeatExecutionActuals_Source_ExternalEventId",
                table: "HeatExecutionActuals",
                columns: new[] { "Source", "ExternalEventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LotGenealogy_ParentLotId_ChildLotId",
                table: "LotGenealogy",
                columns: new[] { "ParentLotId", "ChildLotId" });

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingRouteOperations_ManufacturingRouteId",
                table: "ManufacturingRouteOperations",
                column: "ManufacturingRouteId");

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingRouteOperations_RouteCode_SequenceNumber",
                table: "ManufacturingRouteOperations",
                columns: new[] { "RouteCode", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ManufacturingRoutes_RouteCode",
                table: "ManufacturingRoutes",
                column: "RouteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLotAllocations_MaterialLotId_ProductionOrderId",
                table: "MaterialLotAllocations",
                columns: new[] { "MaterialLotId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLotAllocations_ProductionOrderId",
                table: "MaterialLotAllocations",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialLots_LotNumber",
                table: "MaterialLots",
                column: "LotNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSourcingRules_MaterialCode_GradeCode_CrossSectionCode_DestinationLocationCode",
                table: "MaterialSourcingRules",
                columns: new[] { "MaterialCode", "GradeCode", "CrossSectionCode", "DestinationLocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSourcingRules_RuleCode",
                table: "MaterialSourcingRules",
                column: "RuleCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialSpecifications_MaterialSpecificationCode",
                table: "MaterialSpecifications",
                column: "MaterialSpecificationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OperationDispatchRevisions_PlanVersionId_PlanningKey_ChangedOnUtc",
                table: "OperationDispatchRevisions",
                columns: new[] { "PlanVersionId", "PlanningKey", "ChangedOnUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderChemistryRequirements_ProductionOrderRequirementId_ElementCode",
                table: "OrderChemistryRequirements",
                columns: new[] { "ProductionOrderRequirementId", "ElementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderProcessRequirements_ProductionOrderRequirementId_ProcessOperationType_RequiredResourceId",
                table: "OrderProcessRequirements",
                columns: new[] { "ProductionOrderRequirementId", "ProcessOperationType", "RequiredResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_PackagingSpecifications_PackagingCode",
                table: "PackagingSpecifications",
                column: "PackagingCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCampaignAllocationSnapshots_PlanVersionId_CampaignId_ProductionOrderId",
                table: "PlanCampaignAllocationSnapshots",
                columns: new[] { "PlanVersionId", "CampaignId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanCampaignGradeSequenceSnapshots_PlanVersionId_CampaignId_SequenceNumber",
                table: "PlanCampaignGradeSequenceSnapshots",
                columns: new[] { "PlanVersionId", "CampaignId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCampaignSnapshots_PlanVersionId_CampaignId",
                table: "PlanCampaignSnapshots",
                columns: new[] { "PlanVersionId", "CampaignId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCastSequenceHeatSnapshots_PlanVersionId_CastSequenceId_Position",
                table: "PlanCastSequenceHeatSnapshots",
                columns: new[] { "PlanVersionId", "CastSequenceId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanCastSequenceSnapshots_PlanVersionId_CastSequenceId",
                table: "PlanCastSequenceSnapshots",
                columns: new[] { "PlanVersionId", "CastSequenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanChemistryRequirementSnapshot_PlanOrderRequirementSnapshotId",
                table: "PlanChemistryRequirementSnapshot",
                column: "PlanOrderRequirementSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanDemandCoverageSnapshots_PlanVersionId_SalesOrderId_MaterialCode_GradeCode_CrossSectionCode_LocationCode",
                table: "PlanDemandCoverageSnapshots",
                columns: new[] { "PlanVersionId", "SalesOrderId", "MaterialCode", "GradeCode", "CrossSectionCode", "LocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanDemandSnapshots_PlanVersionId_SalesOrderId",
                table: "PlanDemandSnapshots",
                columns: new[] { "PlanVersionId", "SalesOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanHeatAllocationSnapshots_PlanVersionId_CampaignHeatId_ProductionOrderId",
                table: "PlanHeatAllocationSnapshots",
                columns: new[] { "PlanVersionId", "CampaignHeatId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanHeatSnapshots_PlanVersionId_CampaignHeatId",
                table: "PlanHeatSnapshots",
                columns: new[] { "PlanVersionId", "CampaignHeatId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanInventoryAllocationSnapshots_PlanVersionId_ProductionOrderId_Stage",
                table: "PlanInventoryAllocationSnapshots",
                columns: new[] { "PlanVersionId", "ProductionOrderId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanMaterialUnitSnapshots_PlanVersionId_PlanningKey",
                table: "PlanMaterialUnitSnapshots",
                columns: new[] { "PlanVersionId", "PlanningKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanOperationResourceOptionSnapshots_PlanVersionId_PlanningKey_ResourceId",
                table: "PlanOperationResourceOptionSnapshots",
                columns: new[] { "PlanVersionId", "PlanningKey", "ResourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanOperationSnapshots_PlanVersionId_PlanningKey",
                table: "PlanOperationSnapshots",
                columns: new[] { "PlanVersionId", "PlanningKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanOrderRequirementSnapshot_PlanVersionId",
                table: "PlanOrderRequirementSnapshot",
                column: "PlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanPackagingUnitSnapshots_PlanVersionId_PlannedPackagingUnitId",
                table: "PlanPackagingUnitSnapshots",
                columns: new[] { "PlanVersionId", "PlannedPackagingUnitId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanProcessRequirementSnapshot_PlanOrderRequirementSnapshotId",
                table: "PlanProcessRequirementSnapshot",
                column: "PlanOrderRequirementSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanProductionOrderSnapshots_PlanVersionId_ProductionOrderId",
                table: "PlanProductionOrderSnapshots",
                columns: new[] { "PlanVersionId", "ProductionOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanProductionOrderSnapshots_RequirementSnapshotId",
                table: "PlanProductionOrderSnapshots",
                column: "RequirementSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanRollingPlanAllocationSnapshots_PlanVersionId_RollingPlanId_ProductionOrderId",
                table: "PlanRollingPlanAllocationSnapshots",
                columns: new[] { "PlanVersionId", "RollingPlanId", "ProductionOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanRollingPlanSnapshots_PlanVersionId_RollingPlanId",
                table: "PlanRollingPlanSnapshots",
                columns: new[] { "PlanVersionId", "RollingPlanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanRouteOperationAllocationSnapshots_PlanVersionId_RouteOperationPlanId_ProductionOrderId_CampaignId",
                table: "PlanRouteOperationAllocationSnapshots",
                columns: new[] { "PlanVersionId", "RouteOperationPlanId", "ProductionOrderId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanRouteOperationSnapshots_PlanVersionId_RouteOperationPlanId",
                table: "PlanRouteOperationSnapshots",
                columns: new[] { "PlanVersionId", "RouteOperationPlanId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlantAreas_PlantId_Code",
                table: "PlantAreas",
                columns: new[] { "PlantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanVersionStates_PlanVersionId",
                table: "PlanVersionStates",
                column: "PlanVersionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStages_PlantAreaId",
                table: "ProcessStages",
                column: "PlantAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessStages_PlantId_Code",
                table: "ProcessStages",
                columns: new[] { "PlantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrderRequirements_ProductionOrderId",
                table: "ProductionOrderRequirements",
                column: "ProductionOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_ProductionOrderNumber",
                table: "ProductionOrders",
                column: "ProductionOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_SalesOrderId",
                table: "ProductionOrders",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductionOrders_SteelGradeId",
                table: "ProductionOrders",
                column: "SteelGradeId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCalendars_ResourceId_Start_End",
                table: "ResourceCalendars",
                columns: new[] { "ResourceId", "Start", "End" });

            migrationBuilder.CreateIndex(
                name: "IX_ResourceCapabilities_ResourceId_ProcessOperationType",
                table: "ResourceCapabilities",
                columns: new[] { "ResourceId", "ProcessOperationType" });

            migrationBuilder.CreateIndex(
                name: "IX_Resources_PlantId_Code",
                table: "Resources",
                columns: new[] { "PlantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RollingPlanAllocations_ProductionOrderId",
                table: "RollingPlanAllocations",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RollingPlanAllocations_RollingPlanId_ProductionOrderId_CampaignId",
                table: "RollingPlanAllocations",
                columns: new[] { "RollingPlanId", "ProductionOrderId", "CampaignId" });

            migrationBuilder.CreateIndex(
                name: "IX_RouteResourceCapabilities_RouteCode_ResourceId_ProcessOperationType",
                table: "RouteResourceCapabilities",
                columns: new[] { "RouteCode", "ResourceId", "ProcessOperationType" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderChemistryRequirements_SalesOrderRequirementProfileId_ElementCode",
                table: "SalesOrderChemistryRequirements",
                columns: new[] { "SalesOrderRequirementProfileId", "ElementCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDemandStates_ProductionOrderId",
                table: "SalesOrderDemandStates",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderDemandStates_SalesOrderId",
                table: "SalesOrderDemandStates",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderFinishedGoodsCoverage_SalesOrderDemandStateId_MaterialCode_GradeCode_CrossSectionCode_LocationCode",
                table: "SalesOrderFinishedGoodsCoverage",
                columns: new[] { "SalesOrderDemandStateId", "MaterialCode", "GradeCode", "CrossSectionCode", "LocationCode" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderProcessRequirements_SalesOrderRequirementProfileId_ProcessOperationType_RequiredResourceId",
                table: "SalesOrderProcessRequirements",
                columns: new[] { "SalesOrderRequirementProfileId", "ProcessOperationType", "RequiredResourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderRequirementProfiles_SalesOrderId",
                table: "SalesOrderRequirementProfiles",
                column: "SalesOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_SalesOrderNumber_ItemNumber",
                table: "SalesOrders",
                columns: new[] { "SalesOrderNumber", "ItemNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SteelGrades_GradeCode",
                table: "SteelGrades",
                column: "GradeCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StrandMaterialActuals_HeatExecutionActualId_StrandNumber_UnitSequence",
                table: "StrandMaterialActuals",
                columns: new[] { "HeatExecutionActualId", "StrandNumber", "UnitSequence" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAllocations_ProductionOrderId",
                table: "WorkOrderAllocations",
                column: "ProductionOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderAllocations_WorkOrderId",
                table: "WorkOrderAllocations",
                column: "WorkOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_ExternalExecutionId",
                table: "WorkOrders",
                column: "ExternalExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrders_WorkOrderNumber",
                table: "WorkOrders",
                column: "WorkOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistory_Source_ExternalEventId",
                table: "WorkOrderStatusHistory",
                columns: new[] { "Source", "ExternalEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkOrderStatusHistory_WorkOrderId_ChangedOnUtc",
                table: "WorkOrderStatusHistory",
                columns: new[] { "WorkOrderId", "ChangedOnUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillOfMaterialComponents");

            migrationBuilder.DropTable(
                name: "CampaignAllocations");

            migrationBuilder.DropTable(
                name: "CampaignHeatAllocations");

            migrationBuilder.DropTable(
                name: "CastSequenceHeats");

            migrationBuilder.DropTable(
                name: "CrossSectionSpecifications");

            migrationBuilder.DropTable(
                name: "ExternalMaterialSupplies");

            migrationBuilder.DropTable(
                name: "GradeChemistryRequirements");

            migrationBuilder.DropTable(
                name: "GradeProcessRequirements");

            migrationBuilder.DropTable(
                name: "LotGenealogy");

            migrationBuilder.DropTable(
                name: "ManufacturingRouteOperations");

            migrationBuilder.DropTable(
                name: "MaterialLotAllocations");

            migrationBuilder.DropTable(
                name: "MaterialSourcingRules");

            migrationBuilder.DropTable(
                name: "MaterialSpecifications");

            migrationBuilder.DropTable(
                name: "OperationDispatchRevisions");

            migrationBuilder.DropTable(
                name: "OrderChemistryRequirements");

            migrationBuilder.DropTable(
                name: "OrderProcessRequirements");

            migrationBuilder.DropTable(
                name: "PackagingSpecifications");

            migrationBuilder.DropTable(
                name: "PlanCampaignAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanCampaignGradeSequenceSnapshots");

            migrationBuilder.DropTable(
                name: "PlanCampaignSnapshots");

            migrationBuilder.DropTable(
                name: "PlanCastSequenceHeatSnapshots");

            migrationBuilder.DropTable(
                name: "PlanCastSequenceSnapshots");

            migrationBuilder.DropTable(
                name: "PlanChemistryRequirementSnapshot");

            migrationBuilder.DropTable(
                name: "PlanDemandCoverageSnapshots");

            migrationBuilder.DropTable(
                name: "PlanDemandSnapshots");

            migrationBuilder.DropTable(
                name: "PlanHeatAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanHeatSnapshots");

            migrationBuilder.DropTable(
                name: "PlanInventoryAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanMaterialUnitSnapshots");

            migrationBuilder.DropTable(
                name: "PlannedPackagingUnits");

            migrationBuilder.DropTable(
                name: "PlanOperationResourceOptionSnapshots");

            migrationBuilder.DropTable(
                name: "PlanOperationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanPackagingUnitSnapshots");

            migrationBuilder.DropTable(
                name: "PlanProcessRequirementSnapshot");

            migrationBuilder.DropTable(
                name: "PlanProductionOrderSnapshots");

            migrationBuilder.DropTable(
                name: "PlanRollingPlanAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanRollingPlanSnapshots");

            migrationBuilder.DropTable(
                name: "PlanRouteOperationAllocationSnapshots");

            migrationBuilder.DropTable(
                name: "PlanRouteOperationSnapshots");

            migrationBuilder.DropTable(
                name: "PlantFlowLinks");

            migrationBuilder.DropTable(
                name: "Plants");

            migrationBuilder.DropTable(
                name: "PlanVersionStates");

            migrationBuilder.DropTable(
                name: "ProcessStages");

            migrationBuilder.DropTable(
                name: "ResourceCalendars");

            migrationBuilder.DropTable(
                name: "ResourceCapabilities");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "RollingPlanAllocations");

            migrationBuilder.DropTable(
                name: "RouteResourceCapabilities");

            migrationBuilder.DropTable(
                name: "SalesOrderChemistryRequirements");

            migrationBuilder.DropTable(
                name: "SalesOrderFinishedGoodsCoverage");

            migrationBuilder.DropTable(
                name: "SalesOrderProcessRequirements");

            migrationBuilder.DropTable(
                name: "ScheduledOperations");

            migrationBuilder.DropTable(
                name: "StrandMaterialActuals");

            migrationBuilder.DropTable(
                name: "TransitionRules");

            migrationBuilder.DropTable(
                name: "WorkOrderAllocations");

            migrationBuilder.DropTable(
                name: "WorkOrderStatusHistory");

            migrationBuilder.DropTable(
                name: "BillsOfMaterial");

            migrationBuilder.DropTable(
                name: "CampaignHeats");

            migrationBuilder.DropTable(
                name: "CastSequences");

            migrationBuilder.DropTable(
                name: "ManufacturingRoutes");

            migrationBuilder.DropTable(
                name: "MaterialLots");

            migrationBuilder.DropTable(
                name: "ProductionOrderRequirements");

            migrationBuilder.DropTable(
                name: "PlanOrderRequirementSnapshot");

            migrationBuilder.DropTable(
                name: "PlantAreas");

            migrationBuilder.DropTable(
                name: "RollingPlans");

            migrationBuilder.DropTable(
                name: "SalesOrderDemandStates");

            migrationBuilder.DropTable(
                name: "SalesOrderRequirementProfiles");

            migrationBuilder.DropTable(
                name: "HeatExecutionActuals");

            migrationBuilder.DropTable(
                name: "WorkOrders");

            migrationBuilder.DropTable(
                name: "CampaignGradeSequences");

            migrationBuilder.DropTable(
                name: "PlanVersions");

            migrationBuilder.DropTable(
                name: "ProductionOrders");

            migrationBuilder.DropTable(
                name: "Campaigns");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropTable(
                name: "SteelGrades");
        }
    }
}
