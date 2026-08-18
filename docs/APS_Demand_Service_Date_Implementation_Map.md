# APS Demand and Service-Date Implementation Map

Status: **backend implementation map**

## Current code facts

- `SalesOrder` stores SO number, item, material, grade, final cross-section, ordered/open quantity, required date and customer information.
- `ProductionOrder` stores manufacturing attributes, quantity, required date, priority and optional `SalesOrderId` linkage.
- MTS PO generation is explicit through `IMtsProductionOrderService`.
- Campaign planning accepts `ProductionOrder` inputs; it does not derive MTO POs from Sales Orders.
- Campaign grouping uses required date for ordering and campaign minimum date.
- Heat/rolling schedule tasks generally use the earliest linked PO required date as task `DueUtc`.
- CP-SAT penalizes tardiness weighted by priority.

## Missing canonical chain

```text
SalesOrder item
 -> coverage
 -> MTO ProductionOrder derivation
 -> individual service-date preservation
 -> campaign clubbing
 -> quantity-specific completion/service evaluation
```

## Required implementation ownership

### DemandOrchestrationService
Owns SO item -> MTO PO lifecycle.

### MaterialRequirementPlanner
Owns BOM/material coverage and upstream internal requirements.

### CampaignOptimizer
Owns aggregation/clubbing of POs/internal production requirements.

### ProductionStructurePlanner
Owns shared physical production structure.

### FiniteScheduler
Owns resource/time decisions.

### ServiceEvaluation
Owns PO/SO quantity-level lateness/coverage result from solved output.

This separation prevents SO ingestion, material planning, campaign grouping and finite scheduling from collapsing into one oversized service.
