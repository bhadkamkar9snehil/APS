# .NET Planning Core

This branch introduces the production architecture for APS without deleting the existing prototype.

## Ownership

- SAP/XStudio MES supplies sales orders, inventory/master state, execution actuals and downstream manufacturing truth.
- APS owns Production Orders used for planning, campaigns, campaign allocations, heat structure, caster/mill planning, finite schedules, plan versions and replanning decisions.
- XStudio MES remains the execution system for released Work Orders and physical production/genealogy.

## Planning lineage

```text
MTO: Sales Order -> Production Order -+
                                      +-> Campaign -> Work Orders -> execution
MTS: Stock Policy -> Production Order -+

Execution genealogy:
Work Order -> produced lot -> consumed by downstream Work Order -> child lot -> finished bundle/coil
```

Production Orders retain their source semantics. MTO orders link to a Sales Order/item; MTS Production Orders are generated internally from stock policy and inventory position.

## Inventory feedback

Inventory participates before new SMS production is calculated:

```text
PO quantity
  - compatible finished-goods inventory
  = rolling requirement

rolling requirement
  - compatible billet/slab/intermediate inventory
  = fresh steel output requirement
```

The campaign plan retains each inventory allocation by Production Order, stage, material, grade, section, location and quantity. Existing intermediate inventory can create a rolling-only planning block without creating new heats.

Persisted `MaterialLot` records are also exposed through `IInventorySnapshotProvider`. Heat/cast execution creates `CastIntermediate` lots, so manufactured material becomes inventory feedback for the next plan. The provider abstraction can later be backed by XStudio inventory synchronization without changing the planning kernel.

## Campaign planning and heat formation

Campaign formation currently:

1. Nets finished-goods inventory against open Production Order quantity.
2. Gives MTO precedence over MTS during inventory allocation.
3. Nets compatible cast/intermediate inventory before calculating fresh steel.
4. Records the exact inventory positions allocated to each Production Order.
5. Groups residual production by configurable manufacturing compatibility.
6. Allocates multiple Production Orders into campaigns without losing lineage.
7. Forms grade sequence and heat structure inside campaign planning.

Expected casting yield is owned by `CampaignPlanningPolicy`. Heat quantity represents required steelmaking/casting input; the fresh-steel requirement represents required usable cast output. Heat input is inflated during campaign planning so expected cast output is sufficient for rolling demand.

Compatible exact grades may share a campaign through a configured grade-sequence class. MTO/MTS mixing is policy controlled.

## Coupled production structure

`ProductionStructurePlanningService` converts campaigns into linked production structures:

```text
Campaign heats
  -> caster eligibility
  -> cast sequences
  -> expected billet supply
  -> rolling requirements
  -> mill eligibility/allocation
  -> rolling sequence blocks
```

Casters and mills are capability driven rather than hard-coded. Resource capability can constrain grade/family, route, input/output cross-section and product family. Transition rules provide allowed/forbidden and penalized grade/section changes.

Expected billet quantity for each heat is derived from the yield-aware campaign heat structure so the output across the grade sequence reconciles back to the fresh-steel requirement.

## Configured multi-stage routes

When `RoutePlanningInput` is supplied, APS uses the configured-route planning path instead of the legacy single-stage rolling path.

`ConfiguredRouteProductionStructureBuilder` selects the hot-rolling stage against the route's configured hot-stage input/output sections and stage-specific `RouteResourceCapability` records. This avoids treating the Production Order's final section as though it were necessarily the hot-mill output.

`MultiStageRouteProjector` then expands the hot-rolling plan through configured downstream operations such as ColdRolling and Finishing. Each downstream route stage:

- validates section continuity from the upstream stage,
- selects an eligible active resource from route-specific capability master data,
- creates its own `RouteOperationPlan`,
- preserves Production Order lineage,
- creates one dependent finite-schedule task per upstream material/feed block,
- enforces configured minimum/maximum queue time to its predecessor,
- can be marked as an inventory-decoupling point.

Optional route operations are skipped when their configured output is not required by the Production Order.

The current route implementation deliberately rejects downstream stage yields other than 100% with `DOWNSTREAM_ROUTE_YIELD_NOT_YET_PROPAGATED`; backward quantity propagation through multi-stage routes is not implemented yet.

Resource selection for caster, hot rolling and downstream route stages is still performed heuristically before CP-SAT. The selected resource is therefore normally presented to the finite scheduler as one fixed resource option even though `FiniteScheduleOptimizer` itself supports alternative-resource presence variables.

## Heat, strand and progressive rolling availability

`HeatLevelScheduleProjector` projects each cast sequence into individual heat tasks. Each heat generates planned strand material units using the configured caster strand count.

Fresh rolling is then split into material-feed blocks. Each block consumes planned output from a specific earlier heat and inherits the relevant caster-to-mill transfer constraints. The mill can therefore start consuming material from an earlier heat while later heats in the cast sequence are still being produced.

The projector maintains a remaining-supply pool per heat so planned billet cannot be double-consumed by multiple rolling plans. If expected cast output is insufficient after yield and prior allocations, planning stops with `INSUFFICIENT_PLANNED_CAST_OUTPUT` before CP-SAT scheduling.

Existing-intermediate-inventory rolling blocks have no fresh-cast predecessor.

## Finite scheduling

`FiniteScheduleOptimizer` uses Google OR-Tools CP-SAT for exact time placement:

- resource assignment from eligible options,
- unary finite capacity through `NoOverlap`,
- resource-calendar downtime blocks,
- precedence,
- minimum transfer lag,
- optional maximum transfer/hot-charge lag,
- transition/setup time between planned blocks,
- weighted tardiness,
- assignment penalties,
- makespan minimization,
- frozen-operation hard constraints,
- slushy-zone movement and resource-change penalties.

The optimizer already models one presence variable per `(task, resource option)` and `AddExactlyOne` over eligible options. At present, upstream production-structure builders normally collapse those options to one selected caster/mill/resource before solve, so the alternative-resource capability is largely unused by the end-to-end planning path.

`FiniteScheduleTaskSequencer` currently adds same-resource/same-task-type ordering dependencies before CP-SAT. It deliberately does **not** chain adjacent tasks that share the same `SourceEntityId`: those are feed-block siblings from one upstream plan/route operation, and each already carries its own material/queue predecessor. Different source entities sharing a resource are still chained so configured changeover time remains enforced.

Infeasible plans return an explicit non-feasible result and are not silently converted to a heuristic schedule.

## Plan versions and replanning

Every database-backed planning run is persisted as a Plan Version. The stored snapshot includes:

- parent/baseline Plan Version,
- trigger and reference time,
- horizon and solver result,
- stable planning-operation identity,
- assigned resource/start/end,
- inventory allocations used by the plan,
- planned strand material units and planned availability.

Stable planning keys are derived from business content rather than transient solver GUIDs. Progressive rolling feed blocks receive separate stable identities.

Time fences are applied against the baseline plan:

```text
Frozen -> resource and start remain fixed
Slushy -> movement is allowed but penalized
Liquid -> operation is freely replanned
```

`POST /api/planning/replan/{baselinePlanVersionId}` creates a child Plan Version rather than overwriting history.

Before the replan is solved, `IReplanningActualStateProvider` overlays persisted execution state onto the baseline:

- completed heat/WO operations are removed from the stability baseline,
- running heat/WO operations override planned start/resource with actual execution state,
- current inventory is refreshed through `IInventorySnapshotProvider` by default.

The Production Order open/remaining quantities supplied to the replan are still expected to reflect the latest commercial/production confirmations.

## Plan differences

Persisted versions can be compared by stable planning key. The comparison reports:

- added operations,
- removed operations,
- moved operations,
- resource changes,
- combined move/resource changes,
- unchanged operations,
- start-time movement in minutes and maximum movement.

This provides a schedule-stability view for every replan instead of forcing planners to visually compare two schedules.

## Release and traceability

An approved feasible plan is converted into Work Orders:

- SMS Work Orders carry campaign/grade steelmaking input quantity and are timed from scheduled heats.
- An SMS WO can contain multiple heat-level scheduled operations.
- Hot-rolling Work Orders carry rolling quantities and can contain multiple progressive feed-block operations.
- Configured ColdRolling and Finishing route stages release as their own Work Orders.
- Work Order allocations retain the Production Order contribution.
- MTO Production Orders retain their Sales Order/item link.

Every released `ScheduledOperation` carries the same stable planning key used by plan versions. Database-backed release persists Work Orders, allocations and scheduled operations and marks the Plan Version `Released`.

The XStudio release envelope contains both Work Orders and cast-sequence/heat details.

## Execution feedback

Two execution grains are deliberately separate.

### Work Order execution

Manual execution and MES events use the same Work Order execution service. It stores lifecycle status, actual start/end, actual quantity, audit history, source and idempotent external event IDs.

### Heat/cast execution

Heat execution is tracked independently because one SMS Work Order can contain multiple heats. Heat updates can carry:

- actual heat/cast identifiers,
- actual caster,
- actual start/end and quantity,
- strand/unit output,
- material, grade and cross-section,
- produced lot number and location.

Completed strand outputs are materialized as available `CastIntermediate` material lots. MES retries are idempotent by external event ID and existing lot number.

## Integration boundary

`APS.Integrations` contains the XStudio boundary. APS planning code must not reference XStudio table names or REST details. Deployments may use:

- API events for WO and heat/cast execution changes,
- controlled MES stored procedures/APIs for released plan writes,
- read-only SQL reconciliation for bulk recovery and inventory/actual snapshots.

## Blazor host and planning sandbox

The Blazor application shell lives in `APS.Service`, while reusable layouts and feature pages live in `APS.UI`.

`APS.Service` owns `App.razor` and `Routes.razor`, maps static assets, hosts `_framework/blazor.web.js`, and adds the `APS.UI` assembly to both Razor component endpoint discovery and the client `Router`. This is required for interactive-server behavior and for `@page` routes compiled into the class library to be discoverable.

`/planning` is a working reference/demo page that runs `IPlanningEngine` end-to-end against the built-in long-products sample scenario and can build the corresponding release Work Orders. It is not yet the production planner workspace; DB-backed plan-history, replanning and execution-management pages remain to be built.

## Runtime

- .NET 10
- ASP.NET Core service
- Blazor interactive-server UI
- SQL Server / EF Core
- Google OR-Tools CP-SAT

Current APIs include:

- `GET /api/health`
- `GET /api/inventory/snapshot`
- `POST /api/planning/run`
- `POST /api/planning/replan/{baselinePlanVersionId}`
- `GET /api/planning/versions/{planVersionId}`
- `GET /api/planning/versions/{newPlanVersionId}/compare/{baselinePlanVersionId}`
- `POST /api/planning/mts/production-order`
- `POST /api/planning/campaigns/form`
- `POST /api/planning/structure/build`
- `POST /api/planning/schedule/solve`
- `POST /api/planning/release/build`
- `POST /api/planning/release`
- `POST /api/execution/work-orders/{workOrderId}`
- `POST /api/execution/heats`
- `POST /api/integration/xstudio/execution-events`
- `POST /api/integration/xstudio/heat-events`
- traceability endpoints for Work Orders and material lots

`POST /api/planning/calculate` is **not** currently mapped by `APS.Service`; any higher-level 'load current master data and inventory, then solve' endpoint remains future service work.

## Current boundary / next refinements

The highest-value next solver work is to move resource and sequence choice into CP-SAT instead of giving the optimizer a heuristic fait accompli:

1. Move same-resource task ordering and sequence-dependent transition/setup selection into the optimization model while preserving feed-block sibling semantics and existing explicit material/queue dependencies.
2. Expose multiple eligible mill/resource options from production-structure planning so the existing CP-SAT alternative-resource variables are actually used end-to-end.
3. Extend the same alternative-resource treatment to caster assignment without breaking cast-sequence continuity and material-source identity.
4. Propagate non-100% yields backward through configured downstream route stages.
5. Improve active-operation remaining-duration treatment during replanning.
6. Add richer infeasibility/relaxation explanations.
7. Model individual billet-piece sizing/cut patterns where required rather than only aggregate strand material units.
