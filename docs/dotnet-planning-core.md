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
  = fresh steel requirement
```

The campaign plan retains each inventory allocation by Production Order, stage, material, grade, section, location and quantity. This makes inventory consumption an explicit planning assumption that can be reserved and reconciled against MES inventory rather than an invisible arithmetic deduction.

A campaign therefore tracks both rolling requirement and fresh-steel requirement. Existing intermediate inventory can create a rolling-only planning block without creating new heats.

## Campaign planning

Campaign formation currently:

1. Nets finished-goods inventory against open Production Order quantity.
2. Gives MTO precedence over MTS during inventory allocation.
3. Nets compatible cast/intermediate inventory before calculating fresh steel.
4. Records the exact inventory positions allocated to each Production Order.
5. Groups residual production by configurable manufacturing compatibility.
6. Allocates multiple Production Orders into campaigns without losing lineage.
7. Forms grade sequence and heat structure inside campaign planning, using fresh-steel quantity only.

Compatible exact grades may share a campaign through a configured grade-sequence class. MTO/MTS mixing is policy controlled.

## Coupled production structure

`ProductionStructurePlanningService` converts campaigns into linked production structures:

```text
Campaign heats
  -> caster eligibility
  -> cast sequences
  -> planned billet supply
  -> rolling requirements
  -> mill eligibility/allocation
  -> rolling sequence blocks
```

Casters and mills are capability driven rather than hard-coded. Resource capability can constrain grade/family, route, input/output cross-section and product family. Transition rules provide allowed/forbidden and penalized grade/section changes.

`HeatLevelScheduleProjector` then projects each cast sequence into individual heat tasks. Each heat generates planned strand material units using the configured caster strand count. This means planned material availability exists heat-by-heat and strand-by-strand rather than only at cast completion.

Fresh rolling blocks inherit caster-to-mill transfer dependencies. Existing-intermediate-inventory blocks do not require a new cast predecessor.

## Finite scheduling

`FiniteScheduleOptimizer` uses Google OR-Tools CP-SAT for exact time placement:

- resource assignment from eligible options,
- unary finite capacity through `NoOverlap`,
- resource-calendar downtime blocks,
- precedence,
- minimum transfer lag,
- optional maximum transfer/hot-charge lag,
- selected caster/mill sequence precedence,
- transition/setup time between planned blocks,
- weighted tardiness,
- assignment penalties,
- makespan minimization,
- frozen-operation hard constraints,
- slushy-zone movement and resource-change penalties.

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

Stable planning keys are derived from business content rather than transient solver GUIDs. A replan can therefore match equivalent casting and rolling operations across plan versions.

Time fences are applied against the baseline plan:

```text
Frozen -> resource and start remain fixed
Slushy -> movement is allowed but penalized
Liquid -> operation is freely replanned
```

`POST /api/planning/replan/{baselinePlanVersionId}` creates a child Plan Version rather than overwriting history.

## Release and traceability

An approved feasible plan is converted into Work Orders:

- SMS Work Orders carry campaign/grade fresh-steel quantities and are timed from their scheduled heats.
- An SMS WO can contain multiple heat-level scheduled operations.
- RM Work Orders carry rolling quantities and mill/time assignments.
- Work Order allocations retain the Production Order contribution.
- MTO Production Orders retain their Sales Order/item link.

Database-backed release persists Work Orders, allocations and scheduled operations and marks the Plan Version `Released`. The XStudio release envelope contains both Work Orders and cast-sequence/heat details.

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

Completed strand outputs are materialized as available intermediate `MaterialLot` records. These lots can therefore feed the next inventory-aware planning run. MES retries are idempotent by external event ID and existing lot number.

## Integration boundary

`APS.Integrations` contains the XStudio boundary. APS planning code must not reference XStudio table names or REST details. Deployments may use:

- API events for WO and heat/cast execution changes,
- controlled MES stored procedures/APIs for released plan writes,
- read-only SQL reconciliation for bulk recovery and inventory/actual snapshots.

## Runtime

- .NET 10
- ASP.NET Core service
- Blazor interactive-server UI
- SQL Server / EF Core
- Google OR-Tools CP-SAT

Current APIs include:

- `GET /api/health`
- `POST /api/planning/run`
- `POST /api/planning/replan/{baselinePlanVersionId}`
- `GET /api/planning/versions/{planVersionId}`
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

## Current boundary / next refinements

The next solver work should deepen sequence selection inside optimization rather than selecting structure heuristically first, allow rolling to consume progressively available strand/billet units instead of conservatively waiting on all linked fresh heats, incorporate active execution directly into resource availability during replanning, add plan-difference/infeasibility explanations, and extend the same route model through cold rolling and finishing where required.
