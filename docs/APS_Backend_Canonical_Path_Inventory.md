# APS Backend Canonical Path Inventory

**Status:** canonical backend-path classification  
**Scope:** production planning, persistence, query, approval/release, execution and replan authority  
**Re-baselined:** 23-Aug-2026 against `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`

A path is not production-authoritative merely because it calls the planning kernel. Current implementation-state context is in [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

---

## 1. Canonical production lifecycle

```text
Planning command
  -> IPlanningLifecycleService
       -> IPlanningMasterDataProvider
       -> IInventorySnapshotProvider
       -> IProductionDemandOrchestrationService
       -> IPlanningEngine (Production mode)
       -> IPlanVersionRepository
  -> IPlannerWorkspaceQueryService
  -> IPersistedPlanReleaseService
       -> persisted readiness evaluation
       -> Feasible -> Approved lifecycle transition
       -> release only from active Approved Plan Version
       -> IPlanReleaseRepository
  -> canonical execution services / MES adapters
  -> IReplanningActualStateProvider
  -> IPlanningLifecycleService.ReplanAsync
```

`IPlanningEngine` is the reusable calculation kernel; it is **not** a complete production lifecycle by itself.

---

## 2. Production HTTP/application surfaces

| Surface | Classification | Rule |
|---|---|---|
| `POST /api/planning/calculate` | Canonical | Resolves authoritative demand/master/inventory truth through `IPlanningLifecycleService.CalculateAsync`. |
| `POST /api/planning/run` | Compatibility alias | Same canonical calculation lifecycle; not a second planner. |
| `POST /api/planning/replan/{planVersionId}` | Canonical | Replans from persisted baseline plus actual state/inventory/masters. |
| `GET /api/planning/versions/{planVersionId}` | Canonical read | Reads persisted Plan Version identity/state. |
| plan comparison endpoints / planner compare facade | Canonical read | Compare persisted Plan Versions; do not reconstruct truth in UI. |
| `IPersistedPlanReleaseService.GetReadinessAsync` | Canonical application boundary | Computes persisted release readiness. |
| `IPersistedPlanReleaseService.ApproveAsync` | Canonical application boundary | Moves an active Feasible Plan Version to Approved only when persisted readiness passes. |
| `POST /api/planning/versions/{planVersionId}/release` | Canonical transport | Releases immutable persisted Plan Version truth; requires active Approved state. |
| `POST /api/planning/release/{planVersionId}` | Compatibility alias | Identity-only alias to the same persisted release service. |
| `PlanningExecutionMode.Production` | Canonical kernel mode | Missing required production configuration fails instead of using compatibility/demo behavior. |
| `PlanningExecutionMode.Compatibility` | Demo/test only | Explicit simplified behavior for demo/focused tests. |

The current Plan Versions Blazor page can consume readiness/approval through the application service directly in the hosted app. There is not a second approval implementation in the UI.

---

## 3. Production data authority

| Data | Canonical source |
|---|---|
| Plant/resources/capabilities/calendars/flow/transitions/routes/thermal/scenario masters | `IPlanningMasterDataProvider` / APS database |
| Current qualified inventory | `IInventorySnapshotProvider` |
| Customer/MTO manufacturing demand | `IProductionDemandOrchestrationService` + canonical persisted demand/PO state |
| Known incoming material | authoritative persisted/integration supply facts loaded through the backend path |
| Released/running future internal output during replan | `IReplanningActualStateProvider` |
| Plan history and assumptions | `IPlanVersionRepository` / Plan Version snapshots |
| Execution actuals | canonical execution services and persisted execution/material entities |
| Release readiness | persisted Plan Version/material/supply/service evidence |
| Release structure | immutable Plan Version snapshots |

A production caller must not supply an alternative resource/calendar/inventory/master universe that creates a second planning truth.

### Manufacturing-only production boundary

Current `PlanningLifecycleService` rejects speculative `AllowExternalBuy`, `AllowTransfer` and `AllowManualSupply` planning controls in Production mode. Authoritative purchased/transferred material already present in incoming/inventory data remains usable supply.

This is a current production contract, not merely documentation wording.

---

## 4. Query/read authority

`IPlannerWorkspaceQueryService` is the planner read facade. Contract files are partitions of one read surface, not competing query engines.

The current implementation uses a partial `PlannerWorkspaceQueryService` split by concern. Historical “unavailable planner” facades were removed.

Historical Plan Version reads must prefer persisted assumptions/snapshots for facts whose interpretation would otherwise drift with live masters. Current workbench capacity follows this rule for resource scheduling/calendar assumptions, with an explicit compatibility fallback for older Plan Versions lacking those snapshots.

---

## 5. Local database behavior

The service and desktop host register infrastructure through the canonical `AddApsInfrastructure` path.

- APS uses SQLite for its self-contained application database.
- `LocalApplicationPaths` resolves the current-user application data directory where required.
- EF migrations are applied before normal hosted-service access.
- the application does not maintain a parallel “database unavailable” production service graph.

SQL Server is not a fallback local APS persistence mode in the current product.

---

## 6. Demo/compatibility path

Component-level planners remain useful for tests/demo/reference behavior but are not independent production APIs.

When demo mode is deliberately enabled, `/api/demo/planning/*` and `/demo/planning` may expose direct-kernel operations. They must remain visibly segregated from production planning authority.

Demo/compatibility behavior must never activate because production masters/data are missing.

---

## 7. Plan Version and release authority

Current lifecycle:

```text
Draft
 -> Feasible
 -> Approved
 -> Released
```

with `Failed`/`Superseded` states where applicable.

Production release is identity-only:

```text
PlanVersionId
 -> PlanVersion / PlanVersionState
 -> immutable demand/material/campaign/heat/rolling/route/operation snapshots
 -> persisted readiness evaluation
 -> active Approved state required
 -> IPersistedPlanReleaseService
 -> PlanRelease
 -> IPlanReleaseRepository
 -> released Work Orders + ScheduledOperations
```

The client supplies intent and Plan Version identity, never a reconstructed plan payload.

### Persisted readiness

Readiness currently checks the persisted plan rather than live mutable inventory/masters. It includes:

- Plan Version state/activity;
- material requirement evidence;
- unresolved shortfall/late/unsourced/cycle/non-manufacturable findings;
- supply-action evidence and firm/on-time incoming evidence where applicable;
- MTO production allocation evidence;
- scheduled completion evidence for those allocations;
- completion versus the persisted Production Order required date.

The service-date portion is a real guard but does not make the current generic `RequiredDate` field the final long-term customer-service date architecture.

### Repository boundary

`IPlanReleaseRepository` also defends the Approved lifecycle boundary. A caller cannot bypass the service by directly replaying a release payload for a merely Feasible or inactive plan.

Release remains idempotent for an already released Plan Version: existing Work Orders/operations are reloaded rather than duplicated.

---

## 8. Execution authority

| Surface | Classification | Rule |
|---|---|---|
| `IOperationExecutionService` | Canonical | Operation-grain execution state/resource/time/quantity facts. |
| `IWorkOrderExecutionService` | Canonical | Work-order lifecycle/external execution linkage. |
| `IHeatExecutionService` | Canonical specialization | Heat/casting physical material facts where required. |
| `/api/execution/*` | Transport adapter | Manual/operational inputs to canonical execution services. |
| `/api/integration/.../*-events` | Transport adapter | MES/integration transport into the same execution services. |

Actual thermal/material state from execution participates in canonical replan behavior; #56 now consumes actual billet thermal evidence where available.

#18 remains responsible for completing full downstream physical transformation/genealogy and actual-state closure.

---

## 9. Retired production paths

Do not reintroduce these as fallbacks:

- Python/workbook production authority;
- caller-supplied release plan payloads;
- no-database planner fallbacks;
- “unavailable” query/service facades that emulate missing persistence;
- speculative publisher/execution ports without a concrete integration;
- production exposure of component planners as an alternative lifecycle;
- separate screen-specific planning truth reconstructed in UI.

Historical Python/workbook/UI code remains available at tag `v0.2.5` when deliberate comparison is required.

---

## 10. Canonical boundary regression coverage

Current regression coverage includes:

1. production lifecycle resolves authoritative provider truth and persists Plan Versions;
2. missing routes/configuration fail rather than entering compatibility fallback;
3. speculative commercial BUY/TRANSFER/manual planning is rejected in the manufacturing-only production path;
4. direct Production-mode kernel calls cannot silently use simplified compatibility structure;
5. persisted release is identity-only/idempotent;
6. configured downstream route-operation snapshots release with allocation lineage;
7. Feasible cannot bypass Approved before release;
8. inactive Approved plans cannot release;
9. direct release-payload replay is rejected at the repository boundary;
10. persisted material/supply/service readiness findings block approval/release when unresolved.

---

## 11. Verification authority

The old “do not use CI” wording is obsolete.

Authoritative automated verification is the shared self-hosted Windows Azure DevOps `EOS` agent running repository-owned `build/verify.ps1`. GitHub Actions/hosted CI are not substitutes.

See [`windows-ci.md`](windows-ci.md) and [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md).

---

## 12. Completion rule

APS has one production authority:

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

Every new backend/API/UI capability must attach to this lifecycle. Demo, compatibility and historical/reference code remain explicitly segregated.
