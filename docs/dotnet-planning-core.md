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

The campaign plan retains each inventory allocation by Production Order, stage, material, grade, section, location and quantity. This makes inventory consumption an explicit planning assumption that can later be reserved and reconciled against MES inventory rather than an invisible arithmetic deduction.

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
  -> finite scheduling tasks
```

Casters and mills are capability driven rather than hard-coded. Resource capability can constrain grade/family, route, input/output cross-section and product family. Transition rules provide allowed/forbidden and penalized grade/section changes.

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
- makespan minimization.

Infeasible plans return an explicit non-feasible result and are not silently converted to a heuristic schedule.

## End-to-end planning run

`PlanningEngine` runs the complete calculation from one refreshed snapshot:

```text
Production Orders + inventory + plant/resource masters
  -> campaign formation
  -> production structure
  -> finite schedule
  -> plan version result
```

This is also the intended full replanning path after manufacturing changes: refresh open Production Orders and inventory/execution state, then run a new plan version. Partial/frozen-zone replanning is a later refinement.

## Release and traceability

An approved feasible plan is converted into Work Orders:

- SMS Work Orders carry campaign/grade fresh-steel quantities.
- RM Work Orders carry rolling quantities and mill/time assignments.
- Work Order allocations retain the Production Order contribution.
- MTO Production Orders retain their Sales Order/item link.

The XStudio release envelope contains both Work Orders and cast-sequence/heat details, so execution receives the commercial lineage and the caster production structure.

## Execution feedback

Manual execution and MES events use the same Work Order execution service. It stores:

- lifecycle status,
- actual start/end,
- actual quantity,
- status history,
- update source,
- external event ID and comment.

Status transitions are validated. Terminal states require an explicit correction to move backwards. External event IDs are treated idempotently, including quantity-only events that do not change WO status.

## Integration boundary

`APS.Integrations` contains the XStudio boundary. APS planning code must not reference XStudio table names or REST details. Deployments may use:

- API events for execution changes,
- controlled MES stored procedures/APIs for released plan writes,
- read-only SQL reconciliation for bulk recovery and inventory/actual snapshots.

## Runtime

- .NET 10
- ASP.NET Core service
- Blazor interactive-server UI
- SQL Server / EF Core
- Google OR-Tools CP-SAT

Current APIs:

- `GET /api/health`
- `POST /api/planning/run`
- `POST /api/planning/mts/production-order`
- `POST /api/planning/campaigns/form`
- `POST /api/planning/structure/build`
- `POST /api/planning/schedule/solve`
- `POST /api/planning/release/build`
- `POST /api/execution/work-orders/{workOrderId}`
- `POST /api/integration/xstudio/execution-events`
- traceability endpoints for Work Orders and material lots

## Current boundary / next refinements

The present implementation separates campaign formation, production-structure selection and exact-time optimization, while carrying the selected equipment sequence into CP-SAT. The next iterations should deepen sequence optimization rather than selecting it heuristically first, add strand/billet-level material release, persist/freeze plan versions, support execution-driven partial replanning and improve infeasibility explanation.
