# .NET Planning Core

**Status:** current implementation note  
**Re-baselined:** 23-Aug-2026 against `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`  
**Authority:** subordinate to [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md), [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) and [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md).

The production APS architecture is the .NET solution. The retired Python/workbook prototype is historical at tag `v0.2.5`.

## Production ownership boundary

- integrations provide customer demand, authoritative inventory/incoming-material facts and execution actuals;
- APS owns manufacturing Production Orders, material requirements, campaigns/heats/routes, finite schedules, immutable Plan Versions, readiness/approval/release and replanning decisions;
- MES remains execution authority for physical production events while APS persists the planning/execution feedback needed to replan;
- current Production mode is manufacturing-only and rejects speculative BUY/TRANSFER/manual-supply planning controls.

## Canonical lifecycle

```text
production planning command
 -> IPlanningLifecycleService
      -> IPlanningMasterDataProvider
      -> IInventorySnapshotProvider
      -> IProductionDemandOrchestrationService
      -> IPlanningEngine (Production)
      -> IPlanVersionRepository
 -> IPlannerWorkspaceQueryService
 -> IPersistedPlanReleaseService
      -> persisted readiness
      -> ApproveAsync
      -> ReleaseAsync
      -> IPlanReleaseRepository
 -> canonical execution services
 -> IReplanningActualStateProvider
 -> IPlanningLifecycleService.ReplanAsync
```

`IPlanningEngine` is the canonical calculation kernel but not a production lifecycle by itself.

## Production versus compatibility mode

- `PlanningExecutionMode.Production` requires authoritative configured route/master truth and fails rather than silently using compatibility structure.
- `PlanningExecutionMode.Compatibility` is for explicit demo/focused-test paths.

Demo paths remain segregated under `/api/demo/planning/*` and `/demo/planning` when deliberately enabled.

## Demand and material — current state

Older versions of this note listed #45/#33/#14 as future work. That is obsolete.

Current integrated foundations include:

- #45 SO item -> qualified FG coverage -> MTO Production Order orchestration;
- #33 recursive BOM/material-requirement causality;
- #14 one time-phased material coverage/reservation/future-supply foundation;
- #11 billet/known-incoming contingency.

Current stock is not the planning horizon. Known incoming, committed/released WIP and planned internal production can satisfy later material needs. Uncovered non-manufacturable quantity remains explicit shortfall.

## Campaign and heat planning — current state

#15 candidate Campaign/grade-sequence/heat optimization is integrated.

Campaign formation now uses hard-compatible candidate sets plus explicit service/manufacturing economics rather than treating deterministic sort-and-fill as production authority. Plan Version assumptions retain campaign decision evidence used for later explanation/comparison.

## Configured manufacturing routes — current state

#34 and #58 are integrated foundations.

`ManufacturingRoute` controls operation order/presence both before and after CCM. There is no universal EAF/LRF/VD chain and no architectural pivot at first `HotRoll`.

Valid route shapes can include direct hot charge, configured reheating, billet-only output, downstream finishing, multi-pass or inter-pass heating when the route/master data says so.

## Thermal planning — current state

### Liquid steel

#9 provides the configured liquid-steel thermal/resource-pair foundation.

### Billet thermal state

#56 is complete and integrated. The planner can:

- estimate billet thermal eligibility from source exit state, required rolling-entry state and transfer/wait/holding loss;
- keep direct hot charge when still eligible;
- retry through a configured optional `Reheat` path when thermal aging removes direct-hot eligibility;
- treat unknown/yard material conservatively;
- consume actual measured billet state during replan, overriding stale categorical/planned state;
- persist/read back the decision basis in Plan Version assumptions.

Older text saying #56 remains the next work item is stale.

## Finite scheduling

`FiniteScheduleOptimizer` uses OR-Tools CP-SAT and supports, among current behavior:

- one-of resource assignment from alternatives;
- per-physical-resource sequencing;
- disjunctive and cumulative scheduling semantics based on masters;
- resource calendars and scenario operating state;
- route/material/process dependencies;
- transfer/queue constraints;
- liquid/billet thermal constraints;
- frozen/slushy stability controls;
- service/tardiness, assignment, transition/setup and stability objective terms;
- linked-resource groups for casting continuity where applicable.

Same-type physical resources remain independent `ResourceId` timelines.

## Resource assignment / current primary gap

The current primary backend issue is **#16**.

A solver-selected resource is not operation identity. The remaining generic lifecycle is:

```text
Eligible Resources
 -> Planned Resource
 -> Commitment State
 -> Committed Resource
 -> Actual Resource
 -> auditable redispatch/local repair
```

The CCM pre-selection defect has already been corrected for the casting slice: eligible casters reach CP-SAT and cast-sequence continuity is solver-enforced. #16 remains open because the generic commitment/dispatch/exclusion-evidence/readback lifecycle is not yet complete across all configured operations.

## Plan Versions and historical truth

Every canonical calculation/replan persists an immutable Plan Version with stable planning keys and the applicable demand/material/campaign/heat/route/operation assumptions/snapshots.

Recent hardening ensures historical workbench capacity prefers persisted resource/calendar scheduling assumptions rather than silently joining changed live masters. Older snapshots use an explicit compatibility fallback when those newer assumptions did not exist.

## Plan approval and release

Older text describing release as simply identity-only from a feasible plan is incomplete.

Current lifecycle includes:

```text
Draft -> Feasible -> Approved -> Released
```

`IPersistedPlanReleaseService` owns:

- `GetReadinessAsync`;
- `ApproveAsync`;
- `ReleaseAsync`.

Approval/release evaluate persisted material/supply/service evidence. Release requires an active Approved Plan Version. Repository persistence also rejects direct bypass/replay attempts.

Release still reconstructs Work Orders/process operations from immutable Plan Version snapshots, not live route masters or a caller-built plan payload.

## Execution and replan

Canonical services remain:

- `IOperationExecutionService`;
- `IWorkOrderExecutionService`;
- `IHeatExecutionService`.

Replan combines persisted baseline truth with current authoritative inventory, protected WIP/future output, execution actuals, time-fence/resource/schedule overrides and current masters, then persists a child Plan Version through the same Production-mode engine.

#18 remains responsible for closing full downstream material transformation/genealogy and actual-state feedback.

## Planner read model

`IPlannerWorkspaceQueryService` remains the single planner read facade, split across contract/partial files by concern rather than by competing implementations.

Current UI/read surfaces include Plan Version context, demand/supply, campaigns, physical schedule, Gantt/workbench, work orders/execution, comparison and supporting decision views. #36 remains the completeness gate for every meaningful backend fact/lever.

## Current Gantt/workbench hardening relevant to the planning core

The integrated workbench now has regression coverage for:

- final-state atomic bulk-move validation;
- frozen/time-fence consistency between preview/apply;
- fixed-query-count move validation paths;
- pointer cancellation/blur cleanup;
- historical capacity/readback immutability.

Post-Ponytail removal of several small Gantt layer files was implementation consolidation, not intentional behavior removal; see [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md).

## Verification

The old statement that APS verification is deferred and does not use CI is obsolete.

Authoritative automated verification is the shared self-hosted Windows Azure DevOps `EOS` agent running repository-owned [`../build/verify.ps1`](../build/verify.ps1). GitHub Actions/hosted CI are not substitutes.

Latest recorded evidence for `71e456d...`: Release build 0 warnings/errors, 336/336 tests, self-contained Windows publish, SQLite quick-check OK and live desktop verification of the 105-operation/8-resource released baseline.

## Next backend work

The current ordered sequence starts with **#16**, then #18, #19, #57, #43, #36, #60, #61 and #44, with #39/#40/#41/#42/#32 applied as cross-cutting gates. See [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md).
