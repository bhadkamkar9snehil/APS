# .NET Planning Core

**Status:** Current implementation note  
**Authority:** Subordinate to `APS_Backend_Acceptance_Audit_2026-08-18.md`, `APS_End_to_End_Manufacturing_Planning_Flow.md`, `APS_Backend_Work_Program.md`, and `APS_Backend_Canonical_Path_Inventory.md`.

The production APS architecture is the .NET solution. The retired Python/workbook prototype is available only through Git history at tag `v0.2.5`.

## Production ownership boundary

- SAP/MES-facing integrations provide customer demand, authoritative inventory/incoming-material facts, execution actuals and manufacturing truth.
- APS owns manufacturing Production Orders, campaigns, heat/route planning, finite schedules, immutable Plan Versions, Work Order release and replanning decisions.
- APS is a **manufacturing planner**. It does not recommend procurement or transfer decisions. Material that cannot be covered by qualified authoritative supply or manufactured by configured plant routes is a shortfall.
- MES remains the execution system for released Work Orders and physical production events; APS retains the planning/execution-feedback model needed to replan.

## Canonical production lifecycle

Production planning no longer consists of a public collection of independently callable campaign/structure/schedule/release APIs.

```text
production planning command
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

`IPlanningEngine` remains the reusable calculation kernel. It is not, by itself, a production lifecycle because it does not own authoritative input resolution or persistence.

### Production vs compatibility mode

`PlanningRunRequest` carries `PlanningExecutionMode`:

- `Production` — used by the canonical lifecycle; configured manufacturing-route operations are mandatory.
- `Compatibility` — retained for focused tests and the explicitly enabled demo sandbox. The simplified legacy structure builder may run only in this mode.

A direct Production-mode engine call without configured route planning fails explicitly instead of silently falling back.

## Authoritative input resolution

Production callers provide demand and planning controls. They do **not** provide arbitrary plant/resource/calendar/inventory snapshots.

The lifecycle resolves:

- physical resources, capabilities, calendars, flow links, transition rules and configured manufacturing routes from `IPlanningMasterDataProvider`;
- grade/section/material/packaging masters that are currently wired into that provider;
- current qualified inventory from `IInventorySnapshotProvider`;
- known authoritative incoming material facts from the integration/master path;
- released/running future internal supply during replan from `IReplanningActualStateProvider`.

Remaining end-to-end master-data wiring is tracked by issue #39.

## Demand and material boundary

The current .NET domain already has Sales Orders and Production Orders, but canonical MTO SO-item -> FG coverage -> Production Order orchestration remains issue #45.

Likewise, current campaign/material logic can net FG/intermediate supply and create fresh-steel requirements, but the canonical recursive BOM/material-requirement graph remains issue #33 and the single time-phased material ledger remains issue #14.

Target causality remains:

```text
SO/MTS demand
  -> Production Order manufacturing requirement
  -> recursive BOM/material requirements
  -> one time-phased supply/shortfall ledger
  -> internal production requirements
  -> campaigns/heats/routes
  -> finite schedule
```

Campaign is an aggregation/optimization construct; it is not the original cause of production.

## Campaign and heat planning

Current campaign planning can:

- preserve MTO/MTS Production Order lineage;
- net qualified FG and compatible intermediate supply;
- allocate multiple Production Orders into campaigns;
- respect grade/sequence/route/segregation compatibility;
- create grade sequence and heat structure;
- use yield-aware heat input quantities;
- form heats using equipment-aware envelopes where configured.

Campaign candidate/set selection is still too deterministic and is tracked by issue #15.

## Configured manufacturing routes

Production mode requires configured route operations rather than treating a hard-coded EAF/LRF/VD/CCM chain as universal plant truth.

Current route-domain structures support ordered `ManufacturingRouteOperation` records with:

- `ProcessOperationType`;
- release WO type;
- input/output material and cross-section semantics;
- required/optional operation semantics;
- capability class;
- minimum/maximum queue time;
- inventory-decoupling metadata;
- charge/hot-material requirements;
- yield.

`RouteResourceCapability` binds physical resource eligibility to route/process/grade/material/section/product attributes.

The route-driven topology still needs deeper generalization for different long-product steel plants; that work is issue #34. Downstream non-100% backward yield propagation is also not complete.

## Finite scheduling

`FiniteScheduleOptimizer` uses Google OR-Tools CP-SAT and currently models:

- optional resource assignment variables and `AddExactlyOne`;
- unary finite capacity through `NoOverlap`;
- resource calendars/downtime;
- explicit route/material/process dependencies;
- minimum and optional maximum transfer/queue lags;
- frozen/slushy plan-stability constraints;
- weighted tardiness;
- assignment penalties;
- makespan;
- sequence-dependent setup and transition rules.

### Solver-owned physical-resource sequencing

For fixed-resource queues, solver ordering uses `AddCircuit` **per physical `ResourceId`**, never per resource type.

```text
CCM-1 -> independent circuit
CCM-2 -> independent circuit
RM-1  -> independent circuit
RM-2  -> independent circuit
```

Therefore different casters/mills remain independently and simultaneously schedulable.

For selected adjacent distinct plans `A -> B` on one physical machine:

```text
Start(B) >= End(A) + TransitionTime(A,B)
```

Transition time/penalty is charged only on the selected adjacency. Forbidden directional transitions omit the corresponding arc. Same-`SourceEntityId` progressive feed-block siblings receive no artificial transition/setup charge; their real material/predecessor constraints remain authoritative.

Alternative-resource late binding and commitment/redispatch are tracked by issue #16. Resource scheduling modes beyond universal unary `NoOverlap` are tracked by issue #35.

## Plan Versions

Every production calculation/replan creates an immutable persisted Plan Version.

Plan Version persistence includes, among other current facts:

- parent/baseline relationship and trigger;
- horizon and solver result;
- stable operation planning keys;
- planned resource/start/end;
- eligible resource-option snapshots and dispatch revisions;
- inventory/material-plan facts currently implemented;
- Production Order snapshots;
- Campaign, allocation, grade-sequence and heat snapshots;
- cast-sequence snapshots;
- rolling-plan snapshots;
- **configured route-operation and route-operation-allocation snapshots**;
- planned packaging/material-unit snapshots.

Route-operation snapshots were registered and added to persistence during #38 because production release must not reconstruct downstream work from live masters after the approved plan has changed.

## Canonical production release

Production release is now **identity-only**.

```text
PlanVersionId
  -> immutable persisted Plan Version snapshots
  -> IPersistedPlanReleaseService
  -> PlanRelease
  -> IPlanReleaseRepository
  -> released Work Orders + ScheduledOperations
```

A caller can no longer submit campaigns, production structure and schedule in the production release request.

`IPersistedPlanReleaseService` builds SMS/casting, rolling and configured downstream Work Orders from persisted plan structure/operation/allocation snapshots and uses the persisted effective resource assignment. The release is idempotent: an already-released Plan Version returns its existing persisted WOs/operations.

The old `PlanReleaseBuildRequest` and `IPlanReleaseBuilder` remain for demo/test in-memory release construction only.

## Execution and replanning

Canonical execution services are:

- `IOperationExecutionService` — operation-grain planned/committed/actual state;
- `IWorkOrderExecutionService` — WO lifecycle and external execution linkage;
- `IHeatExecutionService` — casting specialization that also materializes strand/billet output.

Manual endpoints and MES event endpoints are adapters into those same services; they are not separate state stores.

Replan loads:

- persisted baseline Plan Version;
- current inventory;
- completed/running operation state;
- protected remaining output from committed/released/running upstream production;
- time-fence/resource-override policy;
- current authoritative masters.

It then invokes the same Production-mode `IPlanningEngine` and persists a child Plan Version.

## Query/read model

`IPlannerWorkspaceQueryService` is the one planner query facade.

Its view contracts are split across several files (`PlannerWorkspaceContracts`, `PhysicalWorkspaceContracts`, `ExecutionWorkspaceContracts`, `DecisionWorkspaceContracts`) for organization, but these are not competing query implementations.

Current mapped planner read surfaces include current context, recent versions, control tower, demand/supply, campaigns, steelmaking/casting, finite schedule, work orders and plan comparison. Full backend visibility remains issue #36.

## Demo isolation

Demo/reference calculation is explicit opt-in:

```json
{
  "APS": {
    "DemoModeEnabled": false
  }
}
```

When enabled, component/demo endpoints live under:

```text
/api/demo/planning/*
```

and the Blazor calculation sandbox is at:

```text
/demo/planning
```

The sandbox directly uses the calculation kernel in Compatibility mode and may build an in-memory demo release. Its results are deliberately non-authoritative and non-persisted.

Without a configured APS database, production calculate/replan/release endpoints return a service/configuration failure rather than an ephemeral production-looking result.

## Integration boundary

`APS.Integrations` maps transport/vendor-specific data into APS contracts. Planning code does not reference vendor-specific REST/table details.

Current inbound execution events flow through the canonical execution services. `ExecutionActual` remains a transport-neutral mapping DTO. `IExecutionActualProvider` and `IPlanPublisher` currently have no production implementation and are classified future-only ports, not alternate production paths.

## Runtime/API surface after #38

Core production APIs include:

- `GET /api/health`
- `GET /api/inventory/snapshot`
- `GET /api/planning/master-data`
- `POST /api/planning/calculate` — canonical production calculation + persistence
- `POST /api/planning/run` — compatibility alias to the same lifecycle
- `POST /api/planning/replan/{baselinePlanVersionId}`
- `GET /api/planning/versions/{planVersionId}`
- plan comparison endpoints
- `POST /api/planning/versions/{planVersionId}/release` — canonical persisted-plan release
- `POST /api/planning/release/{planVersionId}` — identity-only compatibility alias
- `/api/ui/planner/*` read surfaces
- `/api/execution/*` canonical manual/operations adapters
- `/api/integration/*` MES/integration event adapters
- traceability endpoints.

When demo mode is enabled, non-authoritative component endpoints are available only under `/api/demo/planning/*`.

## Verification status

Focused #38 boundary tests have been checked in for:

- authoritative master/inventory lifecycle ownership;
- Plan Version persistence;
- missing-route production failure;
- manufacturing-only supply policy enforcement;
- direct Production-mode compatibility-fallback rejection;
- identity-only/idempotent persisted release;
- configured downstream route-operation release.

They have **not been executed in this environment**. Per project rule, GitHub Actions/CI is not used for APS verification. Build/test execution is deferred to the intended developer environment.

## Next backend work

After #38 canonicalization, the ordered backend program continues with #45: authoritative MTO SO-item -> qualified FG coverage -> Production Order/service-date orchestration. See `APS_Backend_Work_Program.md` for the full sequence.
