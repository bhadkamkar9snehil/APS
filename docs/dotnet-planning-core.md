# .NET Planning Core

This branch introduces the production architecture for APS without deleting the existing prototype.

## Ownership

- SAP/XStudio MES supplies sales orders, production/inventory/master state, execution actuals and downstream manufacturing truth.
- APS owns Production Orders used for planning, campaigns, campaign allocations, heat structure, plant/resource planning, finite schedules, plan versions and replanning decisions.
- XStudio MES remains the execution system for released Work Orders and physical production/genealogy.

## Planning lineage

```text
MTO: Sales Order -> Production Order -+
                                      +-> Campaign -> Work Orders -> execution
MTS: Stock Policy -> Production Order -+

Execution genealogy:
Work Order -> produced lot -> consumed by downstream Work Order -> child lot -> finished bundle/coil
```

Production Orders retain their source semantics. MTO orders can link to a Sales Order/item; MTS Production Orders are generated internally from stock policy and inventory position.

## Campaign planning

Campaign formation currently provides the first deterministic implementation of the agreed model:

1. Net finished-goods inventory against open Production Order quantity.
2. Give MTO precedence over MTS during inventory allocation.
3. Group residual production by manufacturing compatibility (grade, caster section and route).
4. Allocate multiple Production Orders into a campaign without losing lineage.
5. Form the campaign heat structure inside campaign planning.

The current algorithm is deliberately deterministic and replaceable. The next stage is candidate generation + optimization using OR-Tools, including grade-transition, cross-section, caster and rolling constraints.

## Plant model

Resources are data driven. Casters, rolling mills and future equipment are represented through `Resource`, `ResourceCapability` and `PlantFlowLink`. A caster can declare strand count; equipment quantity is not hard-coded in the solver domain.

## Integration boundary

`APS.Integrations` contains the XStudio boundary. APS planning code must not reference XStudio table names or REST details. Deployments may use:

- API events for execution changes,
- controlled MES stored procedures/APIs for released plan writes,
- read-only SQL reconciliation for bulk recovery and inventory/actual snapshots.

## Runtime

- .NET 10
- ASP.NET Core service
- Blazor interactive server UI
- SQL Server / EF Core
- OR-Tools package in the planning project
- FluentValidation application layer

Initial APIs:

- `GET /api/health`
- `POST /api/planning/mts/production-order`
- `POST /api/planning/campaigns/form`
- `POST /api/integration/xstudio/execution-events`
