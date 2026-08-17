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

A campaign therefore tracks both rolling requirement and fresh-steel requirement. Existing intermediate inventory can create a rolling-only planning block without creating new heats.

## Campaign planning

Campaign formation currently:

1. Nets finished-goods inventory against open Production Order quantity.
2. Gives MTO precedence over MTS during inventory allocation.
3. Nets compatible cast/intermediate inventory before calculating fresh steel.
4. Groups residual production by configurable manufacturing compatibility.
5. Allocates multiple Production Orders into campaigns without losing lineage.
6. Forms grade sequence and heat structure inside campaign planning, using fresh-steel quantity only.

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
- weighted tardiness,
- assignment penalties,
- makespan minimization.

Infeasible plans return an explicit non-feasible result and are not silently converted to a heuristic schedule.

## Release and traceability

An approved feasible plan is converted into Work Orders:

- SMS Work Orders carry campaign/grade fresh-steel quantities.
- RM Work Orders carry rolling quantities and mill/time assignments.
- Work Order allocations retain the Production Order contribution.
- MTO Production Orders retain their Sales Order/item link.

The XStudio release envelope contains both Work Orders and cast-sequence/heat details, so execution receives the commercial lineage and the caster production structure.

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

Current planning APIs:

- `GET /api/health`
- `POST /api/planning/mts/production-order`
- `POST /api/planning/campaigns/form`
- `POST /api/planning/structure/build`
- `POST /api/planning/schedule/solve`
- `POST /api/planning/release/build`
- `POST /api/integration/xstudio/execution-events`
- traceability endpoints for Work Orders and material lots

## Current boundary / next refinements

The present implementation deliberately separates campaign formation, production-structure selection and exact-time optimization. The next iterations should add true sequence-dependent setup constraints inside CP-SAT, rolling/casting sequence improvement loops, strand-level billet generation, execution-driven replanning and richer infeasibility explanation.
