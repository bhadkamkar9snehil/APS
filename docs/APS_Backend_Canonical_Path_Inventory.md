# APS Backend Canonical Path Inventory

**Issue:** #38  
**Status:** Active implementation inventory  
**Scope:** Production planning/query/release/execution authority. UI design is out of scope except explicit demo isolation.

This document classifies every significant backend path as **Canonical**, **Adapter**, **Demo**, **Legacy/Reference**, or **Dead/Superseded**. A path is not production-authoritative merely because it calls the real solver.

## 1. Canonical production lifecycle

```text
Production planning command
  -> IPlanningLifecycleService
       -> IPlanningMasterDataProvider (SQL authoritative masters)
       -> IInventorySnapshotProvider (authoritative current inventory)
       -> IPlanningEngine (calculation kernel)
       -> IPlanVersionRepository (immutable persisted Plan Version)
  -> IPlannerWorkspaceQueryService (read/query facade)
  -> persisted-plan release [being completed in #38]
  -> execution services / MES event adapters
  -> IReplanningActualStateProvider
  -> IPlanningLifecycleService.ReplanAsync
```

### Production calculation endpoints

| Surface | Classification | Rule |
|---|---|---|
| `POST /api/planning/calculate` | Canonical | Calls `IPlanningLifecycleService.CalculateAsync`; caller supplies demand/planning controls, not plant/inventory truth. |
| `POST /api/planning/run` | Adapter | Compatibility alias to the exact same lifecycle. It must never contain separate calculation/persistence logic. |
| `POST /api/planning/replan/{planVersionId}` | Canonical | Calls `IPlanningLifecycleService.ReplanAsync`; baseline, actual inventory, committed in-process supply and masters are resolved by backend. |
| `IPlanningLifecycleService` | Canonical application lifecycle | Owns authoritative input resolution + `IPlanningEngine` invocation + Plan Version persistence. |
| `IPlanningEngine` | Canonical calculation kernel | Reusable deterministic planning kernel. It is not by itself a production lifecycle because it does not own persistence/source-of-truth resolution. |
| `PlanningExecutionMode.Production` | Canonical kernel mode | Used only for production lifecycle-built requests. |
| `PlanningExecutionMode.Compatibility` | Compatibility/demo/test | Preserves the simplified structure path for focused tests and explicitly enabled demo scenarios; not legal as a production host fallback. |

## 2. Production data authority

| Data | Canonical source |
|---|---|
| Plant/resources/capabilities/calendars/flow/transition/route masters | `IPlanningMasterDataProvider` / SQL |
| Steel grades, sections, material and packaging masters | `IPlanningMasterDataProvider` / SQL where currently wired; remaining master wiring tracked by #39 |
| Current qualified inventory | `IInventorySnapshotProvider` |
| Known incoming material | authoritative persisted/integration supply facts loaded through master/inventory path; not caller-invented procurement |
| Released/running future internal output during replan | `IReplanningActualStateProvider` |
| Plan history | `IPlanVersionRepository` |
| Execution actuals | execution services and integration adapters persisted in canonical entities |

A production HTTP caller must not post an arbitrary resource/calendar/inventory/master snapshot and thereby create a second planning truth.

## 3. Query/read authority

`IPlannerWorkspaceQueryService` is the single planner read facade.

The following files are **contract partitions**, not duplicate query implementations:

- `PlannerWorkspaceContracts.cs`
- `PhysicalWorkspaceContracts.cs`
- `ExecutionWorkspaceContracts.cs`
- `DecisionWorkspaceContracts.cs`

The implementation is one partial `PlannerWorkspaceQueryService` split by concern for maintainability.

`UnavailablePlannerWorkspaceQueryService` is not an alternative planner. It exists only to make configuration state explicit and, when demo mode is explicitly enabled, permit the no-database demo shell to render without pretending an empty database is production truth.

## 4. Demo / component paths

Component-level algorithms remain useful, but they are not independent production APIs.

When `APS:DemoModeEnabled=true`, the host may expose:

```text
/api/demo/planning/run
/api/demo/planning/mts/production-order
/api/demo/planning/campaigns/form
/api/demo/planning/structure/build
/api/demo/planning/schedule/solve
/api/demo/planning/release/build
```

These are classified **Demo**. They may return non-persisted calculation results for development/reference use. They must never be exposed as `/api/planning/*` production lifecycle endpoints.

Default configuration is:

```json
{
  "APS": {
    "DemoModeEnabled": false
  }
}
```

The Blazor planning sandbox is likewise a demo/reference surface and must not masquerade as the DB-backed production planner.

## 5. Missing-database behavior

Without an APS SQL connection:

- production calculate/run/replan are unavailable;
- the host returns an explicit configuration/service-unavailable response;
- it does not run `IPlanningEngine` and return an ephemeral production-looking plan;
- empty/null workspace data is not used to disguise missing production configuration;
- the no-DB calculation sandbox is available only under explicit demo opt-in.

## 6. Release authority

### Previous unsafe path

The former production endpoint accepted a `PlanReleaseBuildRequest` containing campaigns, production structure and finite schedule from the caller and persisted WOs from that payload. This allowed release input to diverge from the stored Plan Version.

That public production path has been removed while #38 completes persisted-plan release.

### Target canonical path

```text
persisted PlanVersionId
  -> immutable campaign/heat/route/operation/allocation snapshots
  -> release projection
  -> IPlanReleaseRepository
  -> released Work Orders + ScheduledOperations
```

The client will submit **identity/intent**, not a reconstructed plan.

During the #38 implementation audit, `PlanRouteOperationSnapshot` and `PlanRouteOperationAllocationSnapshot` were found in the domain but not registered/written by current Plan Version persistence. That missing snapshot wiring must be corrected before downstream route release can be reconstructed solely from persisted plan truth.

## 7. Execution authority

| Surface | Classification |
|---|---|
| `IOperationExecutionService` | Canonical operation-grain execution state |
| `IWorkOrderExecutionService` | Canonical WO lifecycle/external execution linkage |
| `IHeatExecutionService` | Canonical heat/cast specialization where strand/material output is required |
| `/api/execution/*` | Canonical manual/operations adapters to the services above |
| `/api/integration/.../*-events` | Adapter | MES/integration transport into the same canonical execution services |

The specialized heat path is retained because casting creates physical strand/billet output; it must not become a second independent status truth.

## 8. Legacy/reference implementation

The Python/workbook stack is **Legacy/Reference**:

- `xaps_application_api.py`
- `engine/`
- workbook-backed BOM/capacity/CTP/campaign/scheduler logic
- `ui_design/`

It remains valuable for parity/reference (especially recursive BOM, CTP and capacity behavior) but is not invoked by the .NET production service and must not be reintroduced as a fallback planner.

## 9. Current #38 implementation findings

| Finding | State |
|---|---|
| No-DB `/api/planning/run` generated non-persisted plan | Corrected: production path now fails explicitly |
| Production caller supplied resources/inventory/masters directly | Corrected: canonical lifecycle resolves authoritative state |
| Component planners exposed under production `/api/planning/*` namespace | Corrected: demo-only namespace + opt-in flag |
| Simplified structure fallback looked production-capable | Classified as compatibility mode; production lifecycle requires route master |
| Multiple read DTO files looked like duplicate query services | Audited: one query facade; files are contract partitions |
| Production release trusted client-supplied plan | Unsafe endpoint removed; persisted release projection still being completed |
| Route-operation snapshot entities existed but were not persisted | Confirmed gap; required for canonical release and #39 master/snapshot consistency |
| Demo Blazor page directly calls kernel | Must remain explicit demo-only and gated by `APS:DemoModeEnabled` |

## 10. Completion gate for #38

#38 is complete only when:

1. production calculation always uses `IPlanningLifecycleService` and persists the Plan Version;
2. production masters/current inventory cannot be overridden by a client payload;
3. no-database production planning fails visibly;
4. component calculations are demo/internal only;
5. simplified structure fallback is impossible through production lifecycle;
6. production release is generated from persisted Plan Version truth;
7. planner read surfaces use `IPlannerWorkspaceQueryService`;
8. execution adapters update canonical execution services/entities;
9. demo sandbox is explicit opt-in and clearly namespaced;
10. legacy Python/workbook code is reference only and never production fallback;
11. root/docs architecture descriptions match this path;
12. focused tests are checked in for the canonical boundary (execution deferred to developer environment; no GitHub Actions/CI).
