# APS Demand Orchestration Gap and Implementation Checklist

Status: **backend gap / implementation checklist**

## Confirmed current-state gap

The .NET domain has `SalesOrder` and `ProductionOrder` and supports `ProductionOrder.SalesOrderId`, but the canonical backend does not yet have one explicit MTO demand-orchestration service that owns Sales Order coverage -> derived MTO Production Order lifecycle.

The current code therefore assumes Production Orders are already available to campaign planning.

MTS PO generation is explicit through `IMtsProductionOrderService`; equivalent MTO derivation must be added canonically.

## Required implementation

- authoritative SO/SO-item ingestion/read path from existing integration;
- idempotent SO item identity `(SalesOrderNumber, ItemNumber)`;
- open quantity/status reconciliation;
- customer/SAP requirement normalization;
- qualified FG allocation/reservation before MTO PO derivation;
- internally-manufacturable finished-product check;
- MTO PO create/update/cancel/reconcile service;
- stable PO numbering/identity policy;
- explicit `CustomerRequiredDate` and `ProductionRequiredBy` semantics;
- firm/released PO change rules;
- explanation/audit of quantity derivation;
- Plan Version snapshots of SO demand + derived PO state;
- read model showing SO -> stock coverage -> PO manufacturing requirement;
- duplicate prevention over repeated sync/planning runs;
- cancellation/quantity/date-change behavior from SAP;
- tests covering partial fulfilment and already-released production.

## Important non-responsibilities

The MTO PO derivation service does not:
- create campaigns;
- decide heat sequence;
- choose resources;
- explode upstream raw-material BOM beyond invoking the canonical material engine;
- prescribe procurement.

Those responsibilities remain separated.

## Date rules to implement

- preserve customer due date at SO item grain;
- derive production-required-by using configured post-production allowance;
- carry PO due date through every Campaign/Heat/Rolling/WO allocation;
- do not replace allocation-level dates with campaign minimum date;
- expose lateness/service by PO allocation.

## Clubbing rules to implement

- PO remains demand/manufacturing lineage unit;
- Campaign is aggregation unit;
- compatible POs may share campaign/heats/rolling plans;
- customer/SO/dedicated segregation can prevent clubbing;
- campaign optimization evaluates due-date spread and per-PO service;
- each allocation retains PO quantity/date/customer identity.
