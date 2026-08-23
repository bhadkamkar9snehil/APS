using System.Text.Json;
using APS.Application;
using APS.Domain;
using APS.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace APS.Planning.Tests;

public sealed class PlanReleaseLifecycleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Feasible_plan_with_material_shortfall_cannot_be_approved()
    {
        await using var db = NewDb();
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { Requirement(MaterialRequirementStatus.Shortfall, shortfall: 12m) },
            Array.Empty<MaterialSupplyRequirement>());
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.GetReadinessAsync(planId);

        Assert.False(readiness.IsReleaseReady);
        Assert.Contains(readiness.Findings, x => x.Code == "MATERIAL_SHORTFALL");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(planId));
        Assert.Equal(
            PlanVersionStatus.Feasible,
            (await db.PlanVersionStates.SingleAsync(x => x.PlanVersionId == planId)).Status);
    }

    [Fact]
    public async Task Feasible_plan_with_internal_make_requirement_can_be_approved()
    {
        await using var db = NewDb();
        var requirement = Requirement(MaterialRequirementStatus.InternalProductionRequired);
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { requirement },
            new[] { Supply(requirement.Id, MaterialSupplyActionType.Make, isFirm: false) });
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.ApproveAsync(planId);

        Assert.True(readiness.IsReleaseReady);
        Assert.Equal(PlanVersionStatus.Approved, readiness.Status);
        Assert.Equal(
            PlanVersionStatus.Approved,
            (await db.PlanVersionStates.SingleAsync(x => x.PlanVersionId == planId)).Status);
    }

    [Fact]
    public async Task Feasible_plan_with_on_time_future_material_can_be_approved()
    {
        await using var db = NewDb();
        var requirement = Requirement(MaterialRequirementStatus.PlannedAvailable);
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { requirement },
            Array.Empty<MaterialSupplyRequirement>());
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.ApproveAsync(planId);

        Assert.True(readiness.IsReleaseReady);
        Assert.Equal(PlanVersionStatus.Approved, readiness.Status);
    }

    [Fact]
    public async Task Non_firm_external_supply_blocks_approval()
    {
        await using var db = NewDb();
        var requirement = Requirement(MaterialRequirementStatus.PlannedAvailable);
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { requirement },
            new[] { Supply(requirement.Id, MaterialSupplyActionType.Buy, isFirm: false) });
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.GetReadinessAsync(planId);

        Assert.False(readiness.IsReleaseReady);
        Assert.Contains(readiness.Findings, x => x.Code == "EXTERNAL_SUPPLY_NOT_FIRM");
    }

    [Fact]
    public async Task Firm_on_time_external_supply_allows_approval()
    {
        await using var db = NewDb();
        var requirement = Requirement(MaterialRequirementStatus.PlannedAvailable);
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { requirement },
            new[] { Supply(requirement.Id, MaterialSupplyActionType.Transfer, isFirm: true) });
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.ApproveAsync(planId);

        Assert.True(readiness.IsReleaseReady);
        Assert.Equal(PlanVersionStatus.Approved, readiness.Status);
    }

    [Fact]
    public async Task Late_external_supply_blocks_approval_even_when_firm()
    {
        await using var db = NewDb();
        var requirement = Requirement(MaterialRequirementStatus.PlannedAvailable);
        var supply = Supply(requirement.Id, MaterialSupplyActionType.Buy, isFirm: true);
        supply.ExpectedReceiptUtc = supply.RequiredReceiptUtc.AddMinutes(1);
        var planId = SeedPlan(db, PlanVersionStatus.Feasible, new[] { requirement }, new[] { supply });
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.GetReadinessAsync(planId);

        Assert.False(readiness.IsReleaseReady);
        Assert.Contains(readiness.Findings, x => x.Code == "EXTERNAL_SUPPLY_LATE");
    }

    [Fact]
    public async Task Release_requires_approved_state()
    {
        await using var db = NewDb();
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { Requirement(MaterialRequirementStatus.AvailableNow) },
            Array.Empty<MaterialSupplyRequirement>());
        await db.SaveChangesAsync();
        var repository = new CapturingReleaseRepository();
        var service = new PersistedPlanReleaseService(db, repository);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReleaseAsync(planId));

        Assert.Contains("must be approved", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repository.Persisted);
    }

    [Fact]
    public async Task Approved_plan_can_release_from_its_persisted_snapshot()
    {
        await using var db = NewDb();
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Approved,
            new[] { Requirement(MaterialRequirementStatus.AvailableNow) },
            Array.Empty<MaterialSupplyRequirement>());
        await db.SaveChangesAsync();
        var repository = new CapturingReleaseRepository();
        var service = new PersistedPlanReleaseService(db, repository);

        var release = await service.ReleaseAsync(planId);

        Assert.Equal(planId, release.PlanVersionId);
        Assert.Same(release, repository.Persisted);
    }

    [Fact]
    public async Task Release_repository_rejects_feasible_plan_bypass()
    {
        await using var db = NewDb();
        var planId = SeedPlan(
            db,
            PlanVersionStatus.Feasible,
            new[] { Requirement(MaterialRequirementStatus.AvailableNow) },
            Array.Empty<MaterialSupplyRequirement>());
        await db.SaveChangesAsync();
        var repository = new PlanReleaseRepository(db);
        var release = new PlanRelease(planId, Array.Empty<WorkOrder>(), Array.Empty<ScheduledOperation>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.PersistAsync(release));

        Assert.Contains("approval is required", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Missing_material_snapshot_evidence_blocks_approval()
    {
        await using var db = NewDb();
        var planId = SeedPlan(db, PlanVersionStatus.Feasible, null, null);
        await db.SaveChangesAsync();
        var service = new PersistedPlanReleaseService(db, new CapturingReleaseRepository());

        var readiness = await service.GetReadinessAsync(planId);

        Assert.False(readiness.IsReleaseReady);
        Assert.Contains(readiness.Findings, x => x.Code == "MATERIAL_EVIDENCE_MISSING");
        Assert.Contains(readiness.Findings, x => x.Code == "SUPPLY_EVIDENCE_MISSING");
    }

    private static ApsDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApsDbContext>()
            .UseInMemoryDatabase($"aps-release-lifecycle-{Guid.NewGuid():N}")
            .Options;
        return new ApsDbContext(options);
    }

    private static Guid SeedPlan(
        ApsDbContext db,
        PlanVersionStatus status,
        IReadOnlyCollection<MaterialRequirement>? requirements,
        IReadOnlyCollection<MaterialSupplyRequirement>? supplies)
    {
        var planId = Guid.NewGuid();
        var now = new DateTime(2026, 8, 23, 8, 0, 0, DateTimeKind.Utc);
        db.PlanVersions.Add(new PlanVersion
        {
            Id = planId,
            VersionNumber = $"PLAN-{planId:N}",
            CreatedOnUtc = now
        });
        db.PlanVersionStates.Add(new PlanVersionState
        {
            PlanVersionId = planId,
            Status = status,
            Trigger = PlanTriggerType.Manual,
            ReferenceTimeUtc = now,
            HorizonStartUtc = now,
            HorizonEndUtc = now.AddDays(7),
            SolverStatus = "Optimal",
            IsActive = true,
            MaterialRequirementsJson = requirements is null ? null : JsonSerializer.Serialize(requirements, JsonOptions),
            MaterialSupplyRequirementsJson = supplies is null ? null : JsonSerializer.Serialize(supplies, JsonOptions)
        });
        return planId;
    }

    private static MaterialRequirement Requirement(
        MaterialRequirementStatus status,
        decimal shortfall = 0m) => new()
    {
        RequirementKey = $"REQ-{Guid.NewGuid():N}",
        SourceType = MaterialRequirementSourceType.ProcessOperation,
        SourceEntityId = Guid.NewGuid(),
        MaterialCode = "BILLET-150",
        GradeCode = "G1",
        CrossSectionCode = "150X150",
        MaterialUom = "MT",
        GrossQuantity = 20m,
        RequiredQuantityMt = 20m,
        RequiredAtUtc = new DateTime(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc),
        Status = status,
        NetRequirementQuantity = shortfall,
        ShortfallQuantity = shortfall,
        ShortfallQuantityMt = shortfall,
        Explanation = status.ToString()
    };

    private static MaterialSupplyRequirement Supply(
        Guid requirementId,
        MaterialSupplyActionType actionType,
        bool isFirm)
    {
        var required = new DateTime(2026, 8, 24, 7, 0, 0, DateTimeKind.Utc);
        return new MaterialSupplyRequirement
        {
            MaterialRequirementId = requirementId,
            MaterialCode = "BILLET-150",
            GradeCode = "G1",
            CrossSectionCode = "150X150",
            ActionType = actionType,
            QuantityMt = 20m,
            PlannedOrderQuantityMt = 20m,
            RequiredReceiptUtc = required,
            ExpectedReceiptUtc = required.AddHours(-1),
            IsFirm = isFirm
        };
    }

    private sealed class CapturingReleaseRepository : IPlanReleaseRepository
    {
        public PlanRelease? Persisted { get; private set; }

        public Task<PlanRelease> PersistAsync(
            PlanRelease release,
            CancellationToken cancellationToken = default)
        {
            Persisted = release;
            return Task.FromResult(release);
        }
    }
}
