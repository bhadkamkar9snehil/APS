# APS Backend Canonical Path Inventory

**Issue:** #38  
**Status:** Canonical backend-path classification  
**Scope:** Production planning, query, release and execution authority. Demo paths are explicitly isolated.

This document identifies the one production path and the intentionally non-production adapters around it. A path is not production-authoritative merely because it calls the planning kernel.

## 1. Canonical production lifecycle

```text
Planning command
  -> IPlanningLifecycleService
       -> IPlanningMasterDataProvider
       -> IInventorySnapshotProvider
       -> IPlanningEngine (Production mode)
       -> IPlanVersionRepository
  -> IPlannerWorkspaceQueryService
  -> IPersistedPlanReleaseService
       -> IPlanReleaseRepository
  -> execution services / MES event adapters
  -> IReplanningActualStateProvider
  -> IPlanningLifecycleService.ReplanAsync
```

### Production endpoints

| Surface | Classification | Rule |
|---|---|---|
| `POST /api/planning/calculate` | Canonical | Resolves authoritative planning truth through `IPlanningLifecycleService.CalculateAsync`. |
| `POST /api/planning/run` | Adapter | Compatibility alias to the same canonical calculation lifecycle. |
| `POST /api/planning/replan/{planVersionId}` | Canonical | Replans from persisted baseline, actual state, inventory and masters. |
| `POST /api/planning/versions/{planVersionId}/release` | Canonical | Releases immutable persisted Plan Version truth only. |
| `POST /api/planning/release/{planVersionId}` | Adapter | Identity-only compatibility alias to the same persisted release service. |
| `IPlanningLifecycleService` | Canonical application lifecycle | Owns authoritative input resolution, kernel invocation and persistence. |
| `IPlanningEngine` | Canonical calculation kernel | Reusable calculation kernel; not a production lifecycle by itself. |
| `IPersistedPlanReleaseService` | Canonical production release | Reconstructs release from immutable Plan Version snapshots. |
| `PlanningExecutionMode.Production` | Canonical kernel mode | Missing required route/configuration truth fails instead of using demo compatibility behavior. |
| `PlanningExecutionMode.Compatibility` | Demo/test only | Retains simplified behavior for explicit demo and focused tests. |

The former body-based production release endpoint accepting caller-supplied plan structure has been removed. `PlanReleaseBuildRequest` and `IPlanReleaseBuilder` remain only for the explicitly enabled in-memory demo/test path.

## 2. Production data authority

| Data | Canonical source |
|---|---|
| Plant, resources, capabilities, calendars, flow, transition and route masters | `IPlanningMasterDataProvider` / APS database |
| Current qualified inventory | `IInventorySnapshotProvider` |
| Known incoming material | Persisted/integration supply facts loaded through the canonical backend path |
| Released/running future internal output during replan | `IReplanningActualStateProvider` |
| Plan history | `IPlanVersionRepository` |
| Execution actuals | Canonical execution services and persisted execution entities |
| Release structure | Immutable Plan Version snapshots |

A production caller must not supply an alternative resource, calendar, inventory or master-data universe and thereby create a second planning truth.

## 3. Query/read authority

`IPlannerWorkspaceQueryService` is the single planner read facade.

The following files are contract partitions, not duplicate query implementations:

- `PlannerWorkspaceContracts.cs`
- `PhysicalWorkspaceContracts.cs`
- `ExecutionWorkspaceContracts.cs`
- `DecisionWorkspaceContracts.cs`

The implementation is one partial `PlannerWorkspaceQueryService`, split by concern. There is no alternative “unavailable planner” query implementation in the active architecture.

When no active Plan Version exists, `GetCurrentPlanAsync` returns `null`; planner pages represent the legitimate empty state themselves.

## 4. Local database behavior

Both the API service and desktop host use `AddApsInfrastructure` as the single registration path.

- APS uses SQLite for its self-contained application database.
- If `ConnectionStrings:APS` is absent, `LocalApplicationPaths.ForCurrentUser()` supplies the application data directory and APS uses `aps.db` there.
- Pending EF Core migrations are applied before hosted services begin querying the database.
- The application therefore does not maintain a second “database unavailable” service graph or UI shell.

SQL Server remains a future MES/integration surface; it is not a fallback APS persistence mode in the current application.

## 5. Demo path

Component-level algorithms remain useful for development and reference, but they are not independent production APIs.

When `APS:DemoModeEnabled=true`, the host may expose:

```text
/api/demo/planning/run
/api/demo/planning/mts/production-order
/api/demo/planning/campaigns/form
/api/demo/planning/structure/build
/api/demo/planning/schedule/solve
/api/demo/planning/release/build
```

The Blazor calculation sandbox is routed only at `/demo/planning`. It is hidden/gated when demo mode is disabled and labels its outputs as ephemeral demo results.

Default configuration remains:

```json
{
  "APS": {
    "DemoModeEnabled": false
  }
}
```

Demo and compatibility behavior must never silently become a production fallback.

## 6. Release authority

Production release is identity-only:

```text
PlanVersionId
  -> PlanVersion / PlanVersionState
  -> immutable PO / campaign / heat / rolling / route-operation / operation snapshots
  -> IPersistedPlanReleaseService
  -> PlanRelease
  -> IPlanReleaseRepository
  -> released Work Orders + ScheduledOperations
```

The client submits intent and Plan Version identity, never a reconstructed plan payload.

`PlanRouteOperationSnapshot` and `PlanRouteOperationAllocationSnapshot` are persisted with the Plan Version so downstream configured-route work releases from approved historical truth rather than live masters.

`IPersistedPlanReleaseService` is idempotent: releasing an already released Plan Version reloads the existing Work Orders and operations rather than generating duplicates.

## 7. Execution authority

| Surface | Classification |
|---|---|
| `IOperationExecutionService` | Canonical | Operation-grain planned, committed and actual execution state. |
| `IWorkOrderExecutionService` | Canonical | Work-order lifecycle and external execution linkage. |
| `IHeatExecutionService` | Canonical specialization | Casting/heat specialization where physical strand and material outputs are required. |
| `/api/execution/*` | Adapter | Manual/operational transport into canonical execution services. |
| `/api/integration/.../*-events` | Adapter | MES/integration transport into the same canonical execution services. |

Integration endpoints accept the current execution-update application contracts directly. The unused speculative `IExecutionActualProvider`, `ExecutionActual` and `IPlanPublisher` abstractions were removed; they should be reintroduced only when a concrete integration requirement exists.

## 8. Retired paths

The Python/workbook stack and earlier UI prototypes were retired from the active tree after the .NET product path became authoritative. Their final historical snapshot remains available at tag `v0.2.5` for deliberate comparison only.

The following earlier architecture has also been retired from the active product path:

- caller-supplied production release payloads;
- no-database production planner fallbacks;
- unavailable-service/query facades used to emulate missing persistence;
- speculative execution/publisher ports without implementations;
- production exposure of component planners under `/api/planning/*`.

Do not reintroduce these as fallback behavior.

## 9. Canonical boundary regression coverage

`tests/APS.Planning.Tests/CanonicalBackendBoundaryTests.cs` covers the important backend authority rules, including:

1. production lifecycle obtains master/inventory truth from providers and persists the Plan Version;
2. missing configured routes fail instead of entering compatibility fallback;
3. speculative commercial supply actions are rejected by the manufacturing-only production lifecycle;
4. direct Production-mode kernel invocation cannot enter the compatibility structure fallback;
5. persisted release is identity-only and idempotent;
6. configured downstream route-operation snapshots release with Work Order and PO-allocation lineage.

These tests are checked in but are not treated as GitHub Actions verification. Build/test/runtime verification is performed in the intended developer environment.

## 10. Completion rule

APS has one declared production authority:

```text
IPlanningLifecycleService
 -> IPlanningEngine (Production mode)
 -> IPlanVersionRepository
 -> IPlannerWorkspaceQueryService
 -> IPersistedPlanReleaseService
 -> canonical execution services
 -> IReplanningActualStateProvider
 -> IPlanningLifecycleService.ReplanAsync
```

Every new backend, API or UI capability must attach to this lifecycle. Demo, compatibility and historical/reference code must remain explicitly segregated.
