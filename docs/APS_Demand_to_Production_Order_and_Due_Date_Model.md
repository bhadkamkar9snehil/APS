# APS Demand → Production Order → Due-Date Model

**Status:** canonical demand/service semantics; MTO orchestration foundation implemented, service-date refinement remains  
**Re-baselined:** 23-Aug-2026 against current `main`

This document originally described the missing #45 MTO demand-orchestration service. That gap is now closed. The document remains authoritative for demand/Production Order meaning and for the **remaining due-date/service refinement**.

---

## 1. Current implementation state

The canonical backend now has `IProductionDemandOrchestrationService` in the production lifecycle.

Implemented foundation includes:

- Sales Order/customer demand normalization;
- qualified FG coverage before manufacturing need;
- MTO Production Order creation/reconciliation for uncovered internally manufacturable finished demand;
- idempotent repeated reconciliation;
- SO/PO lineage and demand snapshots;
- MTS manufacturing requirements as a separate stock-policy path;
- material shortage does not silently shrink the finished manufacturing requirement;
- later Campaign/Heat/Rolling/WO allocations preserve Production Order identity;
- persisted release readiness now checks that MTO manufacturing demand has persisted production allocation/schedule evidence and is not planned later than the persisted Production Order `RequiredDate`.

The **remaining gap is not “create MTO POs.”** It is richer explicit customer-service date semantics and quantity/allocation-grain service measurement through aggregation.

---

## 2. Canonical meaning

### Sales Order item

Commercial/customer demand imported from the authoritative ERP/integration source.

Carries, as available:

- Sales Order/item;
- customer;
- material/product;
- grade/specification;
- final product form/section;
- ordered/open quantity;
- customer required/delivery date;
- customer/SAP requirements;
- status.

### Production Order

APS manufacturing requirement, not a renamed Sales Order.

For MTO:

```text
open customer demand
 - qualified FG coverage
 = internal manufacturing requirement
```

For MTS:

```text
stock policy / replenishment need
 -> internal MTS manufacturing requirement
```

A fully FG-covered SO item does not require a new MTO manufacturing PO just to preserve an SO→PO shape.

---

## 3. MTO cardinality and lineage

Default remains lineage-safe:

- one MTO PO per SO item + homogeneous manufacturing requirement;
- preserve `SalesOrderId`/item identity and quantity;
- do not aggregate unrelated SOs merely to reduce PO count;
- aggregation occurs at Campaign/Heat/Rolling/WO allocation level;
- split a PO only for an explicit manufacturing/commitment/time/route reason.

```text
SO-A -> PO-A --\
               \
SO-B -> PO-B ----> Campaign / Heat / WO allocations
               /
SO-C -> PO-C --/
```

Aggregation must never erase independent customer/date/quantity identity.

---

## 4. Quantity derivation

For each SO item:

```text
OpenCustomerDemand
- qualified finished-goods allocation/coverage
= ManufacturingRequirementQuantity
```

If zero, no new MTO manufacturing PO is needed.

If positive and internally manufacturable, APS creates/reconciles the MTO PO for the uncovered quantity.

If billet/raw material is absent, the PO is **not** silently reduced. Recursive BOM/time-phased material planning exposes the upstream internal requirement or explicit shortfall while the finished manufacturing requirement remains visible.

This is now an implemented production invariant, not only a target.

---

## 5. Current date representation versus target semantics

The current model still uses a generic Production Order `RequiredDate` in important paths, including the current persisted service-readiness guard.

That is useful but **not the desired final date vocabulary**.

Target explicit dates:

- `CustomerRequiredDateUtc` — requested/customer delivery date;
- optional `ConfirmedDeliveryDateUtc` — ERP-confirmed date where available;
- `ProductionRequiredByUtc` — latest acceptable finished manufacturing completion before QA/dispatch/post-production allowance;
- operation/material required-at times — derived backward from the route/schedule.

When no post-production allowance is configured, a legitimate compatibility basis is:

```text
ProductionRequiredByUtc = CustomerRequiredDateUtc
```

but the basis should be explicit rather than hidden behind one overloaded `RequiredDate` property.

---

## 6. Backward timing semantics

The customer due date is a demand/service obligation, not the due date of every upstream activity.

```text
Customer delivery required
 <- QA / dispatch / FG allowance
Finished manufacturing required
 <- downstream route duration / queue
Rolling feed required
 <- rolling / heating / queue
Billet required
 <- casting / refining / steelmaking
Raw materials required
```

Material `RequiredAtUtc` should follow the actual consuming route/operation timing where finite timing is known.

---

## 7. Campaign/service behavior — current versus target

Campaign planning is no longer accurately described as simple production-authoritative sort-and-fill; #15 candidate Campaign/grade-sequence/heat optimization is integrated.

Current planning already considers service/due-date signals together with manufacturing economics, compatibility, transitions, furnace-feasible heats, downstream feasibility and replan stability.

However a shared physical Campaign/heat/rolling task may support multiple POs with different due dates. A single aggregate due date remains insufficient as the final service model.

### Target

Each PO allocation retains:

- quantity;
- its own customer/production required-by date;
- expected completion/coverage basis;
- service status/tardiness contribution.

A shared physical task may aggregate demand, but service evaluation must remain allocation-aware.

---

## 8. Quantity-specific service evaluation

Example:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 18-Sep
```

One 100 MT physical block may serve both, but APS must still know which quantity serves each PO and measure service against each obligation.

The solver can optimize shared physical tasks; the Plan Version/read model must preserve allocation-level customer-service truth.

This is the key remaining refinement behind the note in current release readiness: comparing persisted completion to generic `ProductionOrder.RequiredDate` is a useful safety gate, not the final service architecture.

---

## 9. Campaign clubbing semantics

Campaign clubbing is optimization, not PO creation.

Hard compatibility may include:

- route;
- caster/input format;
- grade/sequence transition rules;
- customer/quality segregation;
- downstream feasibility;
- MTO/MTS policy;
- campaign/heat physical envelopes.

Objective/scoring may include:

- allocation-level due-date/service impact;
- due-date spread/early production;
- grade transitions;
- heat utilization;
- caster continuity;
- downstream feasibility;
- campaign/setup/stability cost.

No selected Campaign may erase the underlying PO/customer/date identities.

---

## 10. Canonical demand orchestration ownership

The current canonical MTO service owns the foundation that the old version of this document proposed:

1. read/normalize current Sales Order demand;
2. calculate open demand;
3. apply qualified FG coverage;
4. determine internally manufacturable finished requirement;
5. create/update/cancel/reconcile derived MTO Production Orders idempotently;
6. preserve SO linkage and requirement snapshots;
7. prevent duplicate POs across repeated planning/sync;
8. preserve firm/released stability according to lifecycle/replan policy;
9. expose demand coverage/derivation through planner read models.

Remaining service-date enhancements should evolve this canonical path, not introduce another demand planner.

---

## 11. End-to-end flow

```text
SAP SO item
 -> normalized customer requirement
 -> qualified FG coverage
 -> MTO PO for uncovered finished manufacturing quantity
 -> recursive BOM / time-phased material requirements
 -> internal upstream manufacturing requirement or explicit shortfall
 -> Campaign / heat / route / rolling structure
 -> finite schedule
 -> immutable Plan Version
 -> persisted service/readiness evidence
 -> Approved / released WOs and operations
 -> actual production / FG state
 -> remaining demand
 -> replan
```

---

## 12. Acceptance scenarios

Current/future acceptance must preserve:

1. 100 MT SO + 100 MT qualified FG -> no new MTO manufacturing PO.
2. 100 MT SO + 30 MT qualified FG -> 70 MT MTO PO.
3. 100 MT SO + no billet/raw material -> still 100 MT MTO PO if finished product is internally manufacturable; upstream shortage remains explicit.
4. Two SO items same grade/section/date window -> separate POs may share Campaign/Heat/WO through allocations.
5. Customer segregation requiring dedicated manufacture prevents inappropriate clubbing.
6. PO-A due 10-Sep and PO-B due 25-Sep can share physical work only while each retains its own service obligation.
7. Repeated demand reconciliation does not duplicate MTO POs.
8. Released/firm PO is not silently resized because inventory changes; adjustment is explicit reconciliation/replan behavior.
9. Current persisted release readiness blocks MTO demand with no production allocation/scheduled completion evidence.
10. Future date refinement distinguishes customer-required versus production-required-by and reports service at allocation grain rather than relying only on one aggregate `RequiredDate`.

---

## 13. Implementation priority

Do **not** reopen #45 as if MTO orchestration were absent.

The remaining date/service work should be addressed where it best fits the active backend program—primarily #36 visibility/read contracts, #19 diagnostics/service explanation, #57 richer comparison and #44 end-to-end service acceptance—without creating a duplicate demand service.
