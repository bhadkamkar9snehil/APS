# APS Backend Canonical Path Inventory

**Status:** Current canonical backend architecture  
**Updated:** 2026-08-22  
**Scope:** Production planning, persistence, query, release, execution, and explicit demo surfaces.

## 1. Canonical production lifecycle

```text
Production planning command
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

There is one production planning authority. Demo/component planners do not form a second production path.

## 2. Persistence invariant

APS is self-contained and always provisions a local SQLite database when no `ConnectionStrings:APS` value is supplied. `AddApsInfrastructure` therefore has one service graph and `MigrateApsDatabaseAsync` always runs at startup.

```text
configured APS connection string
        or
%LocalAppData%/APS-Data/Data/aps.db
        -> EF Core SQLite
        -> migrations
        -> canonical APS repositories/services
```

The former no-database service graph and `Unavailable*` implementations were removed because that runtime state is no longer reachable. SQL Server is not an active APS persistence provider; MES/plant integration can add an adapter when a concrete integration requirement exists.

## 3. Production API surfaces

| Surface | Classification | Rule |
|---|---|---|
| `POST /api/planning/calculate` | Canonical | Calls `IPlanningLifecycleService.CalculateAsync`. |
| `POST /api/planning/run` | Compatibility adapter | Alias to the same canonical lifecycle. |
| `POST /api/planning/replan/{planVersionId}` | Canonical | Replans from persisted baseline plus authoritative current state. |
| `POST /api/planning/versions/{planVersionId}/release` | Canonical | Releases immutable persisted Plan Version truth. |
| `POST /api/planning/release/{planVersionId}` | Compatibility adapter | Alias to the same persisted release service. |
| `/api/ui/planner/*` | Canonical query adapter | Reads through `IPlannerWorkspaceQueryService`. |
| `/api/execution/*` | Canonical execution adapter | Writes through execution services. |
| `/api/integration/xstudio/*-events` | MES adapter | Writes MES events into the same canonical execution services. |
| `/api/traceability/*` | Canonical query adapter | Reads persisted execution/material lineage. |

Compatibility aliases contain no separate planning or persistence behavior.

## 4. Production data authority

| Data | Canonical source |
|---|---|
| Plant/resources/capabilities/calendars/routes | `IPlanningMasterDataProvider` / APS persistence |
| Current qualified inventory | `IInventorySnapshotProvider` |
| Sales-order and MTO demand | `IProductionDemandOrchestrationService` |
| Released/running internal output during replan | `IReplanningActualStateProvider` |
| Plan history | `IPlanVersionRepository` |
| Planner reads | `IPlannerWorkspaceQueryService` |
| Execution actuals | execution services and MES adapters |
| Release structure | immutable Plan Version snapshots |

A production caller must not provide an alternative resource, inventory, or master-data truth that bypasses this lifecycle.

## 5. Planner query architecture

`IPlannerWorkspaceQueryService` is the single planner read facade. Its contract and implementation files are split by concern, not by authority.

The planning workbench composes persisted plan context, demand, campaigns, schedule, material, comparison, exceptions, and operation details from this facade. There is no empty-workspace fallback service.

## 6. Demo surface

`APS:DemoModeEnabled` controls only explicit demo/component endpoints:

```text
/api/demo/planning/run
/api/demo/planning/mts/production-order
/api/demo/planning/campaigns/form
/api/demo/planning/structure/build
/api/demo/planning/schedule/solve
/api/demo/planning/release/build
```

Default:

```json
{
  "APS": {
    "DemoModeEnabled": false
  }
}
```

Demo results are non-authoritative and must not be exposed as production `/api/planning/*` behavior.

## 7. Release authority

Production release accepts Plan Version identity, not a caller-reconstructed plan:

```text
PlanVersionId
  -> immutable plan snapshots
  -> IPersistedPlanReleaseService
  -> PlanRelease
  -> IPlanReleaseRepository
  -> Work Orders + Scheduled Operations
```

`IPersistedPlanReleaseService` remains idempotent. Component-level `IPlanReleaseBuilder` is retained for the explicitly enabled demo/test path only.

## 8. Execution and MES authority

Manual execution endpoints and XStudio/MES event endpoints converge on the same services:

- `IOperationExecutionService`
- `IWorkOrderExecutionService`
- `IHeatExecutionService`

The specialized heat path remains because casting produces physical strand/billet output. Integration transport must adapt to these canonical services rather than create parallel planning or execution state.

## 9. Integration code is demand-driven

Future integration capability is not retained as inactive runtime scaffolding:

- the unused standalone `APS.Integrations` project and its unwired XStudio release DTO/mapper were removed;
- unused future-only `IExecutionActualProvider`, `ExecutionActual`, and `IPlanPublisher` application contracts were removed;
- SQL client/provider packages are not retained solely for possible future use;
- future outbound publication or reconciliation should be added when a concrete integration contract and caller require it.

This does not remove the existing XStudio MES event API surface in `APS.Service`.

## 10. Retired paths

The Python/workbook planner and earlier UI prototypes remain retired from the active production path. Historical snapshots belong in version history/tags, not as runtime fallbacks.

## 11. Canonical-path rule

Any new production feature must attach to this chain:

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

If a second path appears to provide the same authority, delete or adapt it rather than maintain two truths.