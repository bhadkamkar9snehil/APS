# APS Demand Orchestration Implementation Checklist

Status: **implemented on the canonical .NET production path for issue #45**

## Implemented production boundary

The canonical production lifecycle now owns Sales Order coverage -> derived MTO Production Order orchestration through `IProductionDemandOrchestrationService` before campaign planning.

The authoritative path is:

`SO reconciliation -> qualified FG coverage -> MTO Production Order reconciliation -> PlanningEngine -> Plan Version snapshots -> planner read models/release`

Campaign planning receives already-netted manufacturing Production Orders. Finished-goods inventory is removed from the downstream production-kernel inventory input so stock cannot be netted a second time.

## Implemented behavior

- authoritative typed SO/SO-item reconciliation endpoint and persisted read path;
- idempotent SO item identity `(SalesOrderNumber, ItemNumber)`;
- open quantity/status reconciliation;
- customer and requirement normalization, including chemistry/process/customer segregation constraints;
- qualified FG coverage before MTO PO derivation;
- conservative treatment when current inventory evidence cannot prove customer-specific qualification;
- internally-manufacturable finished-product resolution through configured grade, caster section and route masters;
- MTO PO create/update/cancel/reconcile lifecycle;
- stable `MTO-{SO}-{item}` numbering with revisions only after historical cancellation/completion;
- explicit customer-required, confirmed-delivery and production-required-by dates;
- firm/released PO protection: changed SO/FG derivation is surfaced for planner attention rather than silently resizing committed work;
- persisted explanation/evidence of open demand, FG coverage and manufacturing requirement;
- Plan Version snapshots of SO demand and FG coverage plus derived PO/campaign/heat state;
- read model/API showing SO -> stock coverage -> PO manufacturing requirement and downstream campaign/heat allocations;
- duplicate prevention over repeated sync/planning runs;
- cancellation and quantity-change behavior from SAP inputs;
- allocation-level service obligations carrying PO quantity, required date and priority into finite scheduling.

## Important non-responsibilities

The MTO PO derivation service does not:
- create campaigns;
- decide heat sequence;
- choose resources;
- explode upstream raw-material BOM;
- prescribe procurement.

Recursive BOM/material requirement derivation is the next work item under issue #33, followed by the unified time-phased material ledger under issue #14.

## Date rules implemented

- customer due date remains at SO-item grain;
- confirmed delivery date, when supplied, is the service-date basis;
- production-required-by is derived using configured quality/packing/dispatch lead offsets;
- PO required date is carried through campaign, heat, rolling and route-operation allocations;
- finite scheduling receives quantity-aware `FiniteScheduleServiceObligation` records per PO allocation instead of collapsing a shared task to one campaign minimum date;
- planner demand/campaign read models expose the allocation-level dates and priorities.

## Clubbing rules implemented

- PO remains the demand/manufacturing lineage unit;
- Campaign remains the aggregation unit;
- compatible POs can share campaigns/heats/rolling plans while retaining separate allocations;
- customer/SO/dedicated segregation requirements remain part of the requirement signature and can prevent pooling;
- each campaign/heat allocation retains PO quantity/date/customer lineage;
- service/tardiness is evaluated against allocation-level quantity/date obligations.

## Acceptance coverage

Focused tests cover:

1. SO 100 MT + qualified FG 100 MT -> no MTO PO.
2. SO 100 MT + qualified FG 30 MT -> MTO PO 70 MT with exact SO linkage.
3. No FG -> full manufacturing PO even with no component/raw-material availability facts supplied to demand orchestration.
4. Compatible SOs remain separate MTO POs and can be aggregated later.
5. Customer-specific/segregation requirements are preserved and uncertified FG is not guessed as eligible.
6. Quantity-aware service obligations prevent an early allocation from making an entire shared task artificially due early.
7. Repeated SO sync/planning is idempotent.
8. Planned PO cancellation and quantity changes reconcile explicitly.
9. Firmed/released work is protected from later FG or changed demand and requires planner attention when the current derivation differs.
10. Held/late FG is excluded and a shared FG pool is consumed once per orchestration run.
11. A special SO requirement with no explicit route override correctly accepts the PO's resolved default route and does not false-flag an unchanged committed PO.

The repository policy for this work forbids GitHub Actions/CI for verification. Focused tests are checked in; runtime execution remains for the intended developer environment.
