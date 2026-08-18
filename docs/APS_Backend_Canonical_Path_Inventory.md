# APS Backend Canonical Path Inventory

**Issue:** #38  
**Status:** Canonical backend-path classification  
**Scope:** Production planning/query/release/execution authority. UI design is out of scope except explicit demo isolation.

This document classifies every significant backend path as **Canonical**, **Adapter**, **Demo**, **Compatibility**, **Future-only**, **Legacy/Reference**, or **Dead/Superseded**. A path is not production-authoritative merely because it calls the real solver.

## 1. Canonical production lifecycle

```text
Production planning command
  -> IPlanningLifecycleService
       -> IPlanningMasterDataProvider (SQL authoritative masters)
       -> IInventorySnapshotProvider (authoritative current inventory)
       -> IPlanningEngine (calculation kernel, Production mode)
       -> IPlanVersionRepository (immutable persisted Plan Version)
  -> IPlannerWorkspaceQueryService (read/query facade)
  -> IPersistedPlanReleaseService (identity-only persisted release)
       -> IPlanReleaseRepository
  -> execution services / MES event adapters
  -> IReplanningActualStateProvider
  -> IPlanningLifecycleService.ReplanAsync
```

### Production calculation/release endpoints

| Surface | Classification | Rule |
|---|---|---|
| `POST /api/planning/calculate` | Canonical | Calls `IPlanningLifecycleService.CalculateAsync`; caller supplies demand/planning controls, not plant/inventory truth. |
| `POST /api/planning/run` | Adapter | Compatibility alias to the exact same lifecycle. It contains no separate calculation/persistence behavior. |
| `POST /api/planning/replan/{planVersionId}` | Canonical | Calls `IPlanningLifecycleService.ReplanAsync`; baseline, actual inventory, committed in-process supply and masters are resolved by backend. |
| `POST /api/planning/versions/{planVersionId}/release` | Canonical | Releases only immutable persisted Plan Version truth through `IPersistedPlanReleaseService`. |
| `POST /api/planning/release/{planVersionId}` | Adapter | Identity-only compatibility alias to the exact same persisted release service. |
| `IPlanningLifecycleService` | Canonical application lifecycle | Owns authoritative input resolution + `IPlanningEngine` invocation + Plan Version persistence. |
| `IPlanningEngine` | Canonical calculation kernel | Reusable planning kernel. It is not by itself a production lifecycle because it does not own persistence/source-of-truth resolution. |
| `IPersistedPlanReleaseService` | Canonical production release | Reconstructs release solely from immutable Plan Version snapshots and persists released WOs/operations. |
| `PlanningExecutionMode.Production` | Canonical kernel mode | Used by production lifecycle-built requests. Missing route configuration is rejected before simplified structure can execute. |
| `PlanningExecutionMode.Compatibility` | Compatibility/demo/test | Preserves the simplified structure path for focused tests and explicitly enabled demo scenarios; not legal as a production host fallback. |

The former body-based production release endpoint that accepted `PlanReleaseBuildRequest` has been removed. `PlanReleaseBuildRequest`/`IPlanReleaseBuilder` remain only for in-memory demo/test use.

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
| Release structure | immutable Plan Version snapshot tables, not live master data and not caller reconstruction |

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

The Blazor calculation sandbox is now explicitly routed at `/demo/planning`, hidden from navigation when demo mode is disabled, checks the same setting before calculation/release, and labels its outputs as ephemeral demo results.

## 5. Missing-database behavior

Without an APS SQL connection:

- production calculate/run/replan/release are unavailable;
- the host returns an explicit configuration/service-unavailable response;
- it does not run `IPlanningEngine` and return an ephemeral production-looking plan;
- empty/null workspace data is not used to disguise missing production configuration;
- the no-DB calculation sandbox is available only under explicit demo opt-in.

## 6. Release authority

### Previous unsafe path

The former production endpoint accepted a `PlanReleaseBuildRequest` containing campaigns, production structure and finite schedule from the caller and persisted WOs from that payload. This allowed release input to diverge from the stored Plan Version.

That path is no longer part of the production surface.

### Canonical release path

```text
PlanVersionId
  -> PlanVersion / PlanVersionState
  -> immutable production-order/campaign/heat/rolling/route-operation/operation snapshots
  -> IPersistedPlanReleaseService
  -> PlanRelease
  -> IPlanReleaseRepository
  -> released Work Orders + ScheduledOperations
```

The client submits **identity/intent only**, never a reconstructed plan.

During #38, `PlanRouteOperationSnapshot` and `PlanRouteOperationAllocationSnapshot` were found in the domain but missing from SQL registration/persistence. They are now registered in `ApsDbContext` and written by `PlanStructureSnapshotProjector`, so downstream configured route work can be released from the approved immutable Plan Version rather than live masters.

`IPersistedPlanReleaseService` is idempotent: a second release request reloads the already-persisted WOs/operations rather than generating a second set.

## 7. Execution authority

| Surface | Classification |
|---|---|
| `IOperationExecutionService` | Canonical | Operation-grain planned/committed/actual execution state. |
| `IWorkOrderExecutionService` | Canonical | WO lifecycle and external execution linkage. |
| `IHeatExecutionService` | Canonical specialization | Heat/cast specialization where physical strand/material outputs are required. |
| `/api/execution/*` | Adapter | Manual/operational transport into canonical execution services. |
| `/api/integration/.../*-events` | Adapter | MES/integration transport into the same canonical execution services. |

The specialized heat path is retained because casting creates physical strand/billet output; it is not an independent second planning truth.

`ExecutionActual` remains a transport-neutral mapping DTO used by the integration project. It is not the persisted execution state.

## 8. Future-only application ports

Two older application ports have no current production implementation:

- `IExecutionActualProvider` — potential reconciliation/polling input port;
- `IPlanPublisher` — potential outbound publication transport port.

They are explicitly classified **Future-only**, not production authority. Current execution feedback enters through execution event services/endpoints; current release authority stops at persisted APS Work Orders/ScheduledOperations. If these ports are implemented later, they must adapt to the same canonical execution/release lifecycle rather than create parallel state.

## 9. Legacy/reference implementation

The Python/workbook stack is **Legacy/Reference**:

- `xaps_application_api.py`
- `engine/`
- workbook-backed BOM/capacity/CTP/campaign/scheduler logic
- `ui_design/`

It remains valuable for parity/reference (especially recursive BOM, CTP and capacity behavior) but is not invoked by the .NET production service and must not be reintroduced as a fallback planner.

## 10. #38 implementation findings and resolution

| Finding | Resolution |
|---|---|
| No-DB `/api/planning/run` generated non-persisted plan | Production path now returns explicit 503 and does not calculate. |
| Production caller supplied resources/inventory/masters directly | `IPlanningLifecycleService` now resolves authoritative master/inventory state. |
| Component planners exposed under production `/api/planning/*` namespace | Moved to explicit opt-in `/api/demo/planning/*`. |
| Simplified structure fallback looked production-capable | Classified as Compatibility mode; Production mode rejects missing configured route. |
| Multiple read DTO files looked like duplicate query services | Audited: one query facade; files are contract partitions. |
| Production release trusted client-supplied plan | Removed; production release is now identity-only from persisted Plan Version. |
| Route-operation snapshot entities existed but were not persisted | Corrected in `ApsDbContext` + `PlanStructureSnapshotProjector`. |
| Demo Blazor page directly called kernel under `/planning` | Moved to `/demo/planning`, hidden/gated by `APS:DemoModeEnabled`. |
| `PlanningIssue` invalid-structure construction had reversed constructor arguments | Corrected during static #38 consistency pass. |

## 11. Boundary regression coverage checked in

`tests/APS.Planning.Tests/CanonicalBackendBoundaryTests.cs` covers:

1. production lifecycle obtains master/inventory truth from providers and persists the Plan Version;
2. missing configured route fails instead of entering compatibility fallback;
3. speculative BUY/TRANSFER/manual supply policy is rejected by production manufacturing-only lifecycle;
4. direct Production-mode kernel invocation cannot enter compatibility structure fallback;
5. persisted release is identity-only and idempotent;
6. configured downstream route-operation snapshots release as downstream WOs with PO allocation lineage.

These tests are **checked in but not executed here**. Per project rule, GitHub Actions/CI is not used; build/test execution is deferred to the intended developer environment.

## 12. Canonical path completion rule

The backend now has one declared production authority:

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

Any new backend/API/UI feature must attach to that lifecycle. Demo, compatibility and legacy/reference code must remain explicitly segregated.
