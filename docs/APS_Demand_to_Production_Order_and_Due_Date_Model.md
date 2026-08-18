# APS Demand → Production Order → Due-Date Model

Status: **canonical design clarification / backend gap**

Scope: backend planning semantics only. No UI implementation.

## 1. Why this document exists

The current .NET domain contains both `SalesOrder` and `ProductionOrder`, and `ProductionOrder` may reference `SalesOrderId`, but the canonical backend does not yet have a sufficiently explicit MTO demand-orchestration service that derives and maintains manufacturing Production Orders from Sales Order items.

This must be made explicit because `ProductionOrder` means **manufacturing requirement**, not a renamed Sales Order and not a generic demand record.

## 2. Canonical meaning

### Sales Order item

Commercial/customer demand imported from SAP.

Carries at least:
- Sales Order number/item;
- customer;
- material/product;
- grade/specification;
- final cross-section/product form;
- ordered/open quantity;
- customer required/delivery date;
- SAP/customer-specific requirements;
- status.

### Production Order

APS manufacturing requirement.

For MTO it exists only for the quantity that must be manufactured internally after qualified finished-goods coverage is considered.

For MTS it is generated from stock-policy replenishment need.

Canonical flow:

```text
SAP Sales Order item
  -> open customer requirement
  -> qualified FG coverage
  -> uncovered finished-product quantity
  -> internally manufacturable?
       -> yes: MTO Production Order
       -> no: visible finished-product shortfall
```

A fully FG-covered Sales Order does not require new manufacturing merely to preserve an SO→PO shape.

## 3. MTO PO cardinality

Default policy should remain simple and lineage-safe:

- one MTO PO per Sales Order item + homogeneous manufacturing requirement;
- preserve `SalesOrderId` and exact quantity;
- do **not** aggregate unrelated SOs into a PO;
- aggregation belongs at Campaign/Heat/Rolling Plan level through allocation entities;
- split a PO only when there is a real planning reason: quantity/time split, route split, customer segregation, partial firm/released state, or another explicit manufacturing boundary.

This preserves the canonical flow:

```text
SO item A -> PO-A --\
                 \
SO item B -> PO-B ----> Campaign -> Heat(s) -> WO(s)
                 /
SO item C -> PO-C --/
```

The Campaign/WO allocations preserve exact PO/SO quantities through aggregation.

## 4. Quantity derivation

For each SO item:

```text
OpenCustomerDemand
- qualified FG already allocated/available for that SO requirement
= ManufacturingRequirementQuantity
```

If zero, no new MTO PO is required.

If positive and the finished material is internally manufacturable, create/update the MTO PO for that quantity.

The PO quantity is **not** reduced because billet/raw material is absent. Upstream material shortages are discovered by recursive BOM/time-phased material planning and remain visible as shortfalls while the finished manufacturing requirement continues to exist.

## 5. Required-by date semantics

Do not collapse all dates into one ambiguous `RequiredDate` long-term.

Recommended canonical dates:

- `CustomerRequiredDateUtc` / requested delivery date — from SAP SO item;
- optional `ConfirmedDeliveryDateUtc` — if SAP provides it;
- `ProductionRequiredByUtc` — latest acceptable completion of finished manufacturing before post-production/QA/dispatch allowance;
- operation/material need times — derived backward from the solved/estimated production route.

Initially, if no post-production allowance master exists:

```text
ProductionRequiredByUtc = CustomerRequiredDateUtc
```

but the basis must be explicit.

## 6. Backward timing

The SO due date is a demand/service constraint, not the due date of every upstream task.

Correct relationship:

```text
Customer delivery required
  <- FG/QA/dispatch allowance
Finished production required
  <- downstream operation duration/queue
Rolling material required
  <- rolling/RHF duration/queue
Billet required
  <- casting/refining/steelmaking route
Raw materials required
```

The material/BOM engine therefore derives `RequiredAtUtc` backward through the consuming operation/route where finite timing is known.

## 7. Current implementation behavior and limitation

Current campaign planning sorts POs roughly by:

1. MTO before MTS;
2. higher Priority;
3. earlier `RequiredDate`;
4. grade / PO number.

Campaign compatibility currently partitions by:
- grade sequence class;
- caster section;
- route;
- exact grade or mixed-grade policy;
- MTO/MTS mixing policy;
- customer/SO/dedicated segregation policy.

Campaigns are then filled up to maximum campaign quantity. Campaign `RequiredDate` becomes the earliest PO required date in the campaign.

Hot-rolling groups similarly retain compatible grade/input/output/route/product-family/feed-source combinations and use the earliest PO date as the task due date.

CP-SAT penalizes task tardiness using `FiniteScheduleTask.DueUtc` weighted by task priority.

This is a useful baseline, but it is **not sufficient service-date modeling** because an aggregated campaign/heat/rolling task can support several POs with different due dates. Using only the earliest date can over-constrain later demand and hide quantity-specific service performance.

## 8. Target due-date behavior for aggregation

Every PO allocation retains its own required-by date.

A Campaign may contain orders with different dates only when:
- all hard compatibility/segregation rules allow it;
- the finite schedule can still satisfy each PO's own service requirement, or a deliberate lateness tradeoff is visible in the objective;
- due-date spread/campaign holding cost is considered in candidate scoring.

Do not turn a campaign's earliest date into the sole due date of every tonne in that campaign.

Recommended campaign facts:
- earliest required date;
- latest required date;
- weighted/service-critical date distribution;
- PO allocation list with quantity + individual required-by date.

## 9. Quantity-specific service evaluation

Service should ultimately be evaluated at demand allocation grain:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 18-Sep
```

If one 100 MT rolling/campaign block serves both, APS must still know which downstream output quantity satisfies PO-A versus PO-B and measure tardiness against each PO separately.

The solver may use shared physical tasks, but the objective/read model must preserve allocation-level service.

## 10. Campaign clubbing target

Campaign clubbing is optimization, not PO creation.

Candidate compatibility includes:
- manufacturing route;
- caster/input format;
- grade/sequence class and transition rules;
- customer/quality/segregation restrictions;
- downstream mill/outlet feasibility;
- MTO/MTS policy;
- campaign/heat capacity limits.

Candidate scoring includes:
- individual PO due-date feasibility/tardiness;
- due-date spread;
- grade transitions;
- heat utilization;
- caster/tundish continuity;
- rolling/downstream feasibility;
- campaign count/setup/stability.

The selected campaign may aggregate many POs, but no PO loses its own quantity/date/customer identity.

## 11. Required backend service

Introduce one canonical MTO demand-orchestration service responsible for:

1. ingest/read current Sales Order items;
2. calculate open customer demand;
3. apply qualified FG coverage/reservations;
4. determine internally manufacturable finished requirement;
5. create/update/cancel derived MTO Production Orders idempotently;
6. preserve SO item linkage and requirement snapshots;
7. derive `ProductionRequiredByUtc` using configured delivery/QA allowance;
8. prevent duplicate POs across repeated SAP sync/planning runs;
9. retain firm/released PO stability during replan;
10. expose demand coverage and PO derivation explanation.

MTS remains separate: stock policy -> internal MTS Production Order.

## 12. End-to-end demand flow

```text
SAP SO item
  -> normalize customer requirement
  -> qualified FG coverage
  -> MTO manufacturing PO for uncovered finished quantity
  -> recursive BOM / time-phased material requirements
  -> internally manufacturable upstream requirements + shortfalls
  -> campaign candidate optimization
  -> heat/route/rolling structure
  -> finite schedule
  -> WOs / operation allocations
  -> actual production / FG inventory
  -> SO fulfilment / remaining demand
  -> replan
```

## 13. Acceptance scenarios

1. 100 MT SO, 100 MT qualified FG -> no new MTO PO; demand covered by stock.
2. 100 MT SO, 30 MT FG -> 70 MT MTO PO.
3. 100 MT SO, 0 billet/raw material -> still 100 MT MTO PO if FG can be manufactured internally; BOM exposes upstream production/shortfall.
4. Two SO items same grade/section/due window -> two POs remain separate, may share one campaign/heat/WO through allocations.
5. Same grade but customer segregation requires dedicated campaign -> POs never club.
6. PO-A due 10-Sep and PO-B due 25-Sep can share a campaign only if service/campaign optimization justifies it; each retains own due date.
7. Repeated SAP sync does not create duplicate MTO POs.
8. Released/firm PO is not silently resized because inventory later changes; adjustment becomes an explicit replan/reconciliation decision.
