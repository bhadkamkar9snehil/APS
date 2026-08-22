# .NET Planning Core

**Status:** Current implementation note  
**Authority:** Subordinate to `APS_Backend_Acceptance_Audit_2026-08-18.md`, `APS_End_to_End_Manufacturing_Planning_Flow.md`, `APS_Backend_Work_Program.md`, and `APS_Backend_Canonical_Path_Inventory.md`.

This document records implementation shape only. Domain rules and production-authority decisions belong in the canonical documents above rather than being repeated here.

## Runtime shape

APS is the .NET solution under `src/`:

```text
APS.Domain          manufacturing/planning model
APS.Application     application contracts
APS.Planning        material, campaign, route and finite scheduling
APS.Infrastructure  SQLite persistence, repositories, providers and read models
APS.Service         ASP.NET Core/Blazor host, APIs and MES event adapters
APS.UI              shared Blazor product UI
APS.DesktopHost     installed WPF/Blazor desktop host
```

The active persistence provider is EF Core SQLite. When `ConnectionStrings:APS` is absent, APS provisions `%LocalAppData%/APS-Data/Data/aps.db` and applies migrations at startup.

Finite scheduling uses OR-Tools CP-SAT.

The retired Python/workbook implementation remains available only through Git history at tag `v0.2.5`.

## Canonical production lifecycle

```text
planning command
  -> IPlanningLifecycleService
       -> IPlanningMasterDataProvider
       -> IInventorySnapshotProvider
       -> IProductionDemandOrchestrationService
       -> IPlanningEngine (Production mode)
       -> IPlanVersionRepository
  -> IPlannerWorkspaceQueryService
  -> IPersistedPlanReleaseService
  -> execution services / MES event adapters
  -> IReplanningActualStateProvider
  -> IPlanningLifecycleService.ReplanAsync
```

Production callers supply demand selection and planning controls. Plant masters, inventory, committed in-process supply and persisted Plan Version truth are resolved behind the lifecycle boundary.

## Planning kernel boundary

`IPlanningEngine` is the reusable calculation kernel, not a second production lifecycle.

`PlanningExecutionMode.Production` requires configured manufacturing-route operations. Compatibility behavior is retained only for focused tests and the explicitly enabled demo sandbox; production must not silently fall back to a simplified fixed route.

The planning kernel preserves the core APS semantics:

- demand/material requirements are the cause of production;
- inventory and known future supply are time-phased rather than treated as a current-stock gate;
- uncovered manufacturable requirements create upstream internal production needs;
- true uncovered/unmanufacturable quantities remain explicit shortfalls;
- campaign formation optimizes compatible demand without replacing demand lineage;
- configured manufacturing routes determine process topology;
- resource assignment is an eligible choice, not production-requirement identity;
- finite scheduling retains alternative eligible resources needed for later operational redispatch;
- thermal, material, calendar, transition and time-fence constraints remain planning facts;
- Plan Versions persist the solved manufacturing truth used by release and replan.

Detailed material, campaign, route, thermal and operational-flexibility behavior is maintained in the canonical domain/backend documents, not duplicated here.

## Production and demo surfaces

Production planning uses the persisted lifecycle and identity-only release endpoints. Component-level planners and direct-kernel calculation are exposed only under the explicit demo surface when `APS:DemoModeEnabled=true`.

The live MES/XStudio integration surface is the execution-event API in `APS.Service`. It writes into the same canonical operation, Work Order and heat execution services as manual execution. There is no separate active integration assembly or parallel execution model.

Future outbound publication or reconciliation adapters should be introduced only when a concrete transport contract and caller exist.

## Verification

Focused regression and acceptance tests remain in `tests/APS.Planning.Tests` and `tests/APS.UI.Tests`.

Per project rule, GitHub Actions/CI is not used as APS project verification. Do not claim a branch is green until build/test/runtime verification has been run in the intended developer environment.