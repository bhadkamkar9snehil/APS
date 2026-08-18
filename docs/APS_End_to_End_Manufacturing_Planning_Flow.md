# APS End-to-End Manufacturing Planning Flow

Status: **canonical manufacturing-planning flow**

Scope: backend/domain/planning only. UI implementation is intentionally excluded.

This document closes an important architectural ambiguity: APS is a **manufacturing planning and scheduling system**, not a procurement recommendation engine and not a logistics-transfer recommendation engine.

APS consumes authoritative inventory and known incoming/material-state integrations. APS may create upstream **internal manufacturing requirements** for materials it knows how to produce. If a requirement is not covered and APS has no configured internal manufacturing route that can satisfy it in time, APS exposes an explicit **shortfall**. It does not recommend `BUY`, `TRANSFER`, supplier selection, procurement quantity or commercial sourcing action.

---

## 1. Canonical planning objective

Given demand and current manufacturing/inventory state, APS must answer:

1. What needs to be produced?
2. What material is already available or is already known to become available?
3. What uncovered material can be manufactured internally?
4. What upstream production does that imply recursively through the BOM?
5. What quantity cannot be covered by inventory/known receipts/internal manufacturing and therefore remains a shortfall?
6. How should the internally manufacturable quantity be grouped into campaigns/heats/lots?
7. On which eligible resources and at what times should every constrained manufacturing operation run?
8. What Work Orders/operations should be released?
9. What actually happened?
10. What remains to be planned after actual production, WIP and inventory are refreshed?

APS must never delete demand merely because material is unavailable today.

---

## 2. End-to-end canonical flow

```text
SAP Sales Order / MTS stock requirement
        |
        v
Normalized Production Order demand
        |
        +--> customer / SO-item requirements
        +--> grade / section / product / packaging requirements
        |
        v
Recursive BOM / material-requirement graph
        |
        v
Time-phased material netting at every BOM node
        |
        +--> on-hand qualified inventory
        +--> known incoming inventory/supply from integration
        +--> released/running internal production receipts
        +--> APS-planned internal production receipts
        |
        v
Uncovered quantity
        |
        +--> internally manufacturable in configured plant?
        |        |
        |        +--> YES -> create upstream internal production requirement
        |        |           and recurse into its BOM
        |        |
        |        +--> NO  -> SHORTFALL
        |
        v
Internal production requirements at schedulable production stages
        |
        v
Campaign candidate generation / campaign selection
        |
        v
Furnace-capacity / route-feasible heat formation
        |
        v
Configured manufacturing route
(primary steelmaking -> secondary metallurgy -> CCM -> billet -> RHF/hot charge -> RM -> downstream finishing)
        |
        v
Finite resource scheduling
        |
        v
Plan Version
        |
        v
Work Orders + operation-level execution rows
        |
        v
Execution actuals / material output / genealogy
        |
        v
Inventory + WIP + remaining demand refresh
        |
        v
Local repair / replan
```

---

## 3. Manufacturing-only material semantics

Every material requirement should resolve to one of these planning states:

### CoveredNow
Qualified material is already available in authoritative inventory.

### CoveredByKnownReceipt
Material is not available now, but an authoritative known receipt or already-committed production receipt will make it available before the requirement time.

Examples:
- released/running internal heat output;
- already-known external/incoming inventory from the integration layer;
- material already in transit if the inventory integration exposes it as an authoritative receipt.

APS does **not** decide that this receipt should be purchased or transferred. It only consumes the known supply fact.

### PlannedInternalProduction
The material is not yet available, but APS knows from BOM + manufacturing-route masters that the material can be manufactured internally. APS creates the required upstream production and schedules it before the consumer need time where feasible.

### LateInternalSupply
The internal production path exists but cannot produce the material by the required time under current finite capacity/constraints.

### Shortfall
The requirement remains uncovered after inventory, known receipts, committed production and feasible APS-planned internal production.

### NotManufacturableHere
APS has no configured internal manufacturing route for the material at this installation. The requirement remains visible as a shortfall/information requirement. A commercial team or another system may decide what to do; APS does not prescribe procurement or transfer.

---

## 4. Recursive BOM is material planning, not mandatory finite scheduling

The BOM may legitimately contain:

```text
Finished coil/bar
 -> rolled intermediate
 -> billet/bloom
 -> liquid steel
 -> steelmaking charge
 -> hot metal / DRI / HBI / scrap / alloys / fluxes
 -> BF burden / sinter / pellets / coke
 -> iron ore / coal / limestone / other leaf raw materials
```

APS must calculate the complete requirement tree through every configured BOM level unless quantity is covered by qualified supply at an intermediate node.

This does **not** mean APS must finite-schedule every stage represented in the BOM.

Example deployment:

```text
BOM/material-planning depth:
FG -> billet -> liquid steel -> hot metal -> burden -> ore/coal

Finite-scheduling depth:
EAF/LRF/VD/CCM/RHF/RM/finishing
```

Hot metal or iron ore may therefore appear as a time-phased requirement/shortfall without a corresponding Work Order if that producing process is outside the configured APS scheduling scope.

If a future installation configures BF or another upstream production stage as an APS-scheduled manufacturing route, the same material graph can create internal production requirements for that stage without redesigning BOM logic.

---

## 5. Material netting occurs before unnecessary upstream manufacture

At every BOM node:

```text
Gross requirement
 - usable inventory
 - known incoming supply
 - committed internal future supply
 - already-planned internal supply
 = uncovered requirement
```

Only the uncovered requirement is exploded/manufactured further.

Examples:

### Finished inventory covers SO
No manufacturing is required.

### Finished inventory absent, billet available
Plan rolling/downstream production only. Do not form unnecessary SMS heats.

### Billet absent, internal billet production possible
Create the required internal steelmaking/casting plan.

### Billet absent, SMS down, known billet receipt exists
Use the known future billet receipt if it arrives in time; schedule RHF/RM accordingly.

### Billet absent, SMS down, no known billet receipt
The rolling requirement remains visible with a billet shortfall. APS does not invent a procurement recommendation.

---

## 6. Required-at time is fundamental

Material feasibility is time-phased, not based on planning-run start inventory.

A one-month campaign can consume progressively produced material.

Example:

```text
01-Sep opening billet          0 MT
04-Sep internal cast receipt  65 MT
09-Sep internal cast receipt  65 MT
15-Sep internal cast receipt  65 MT

RM need:
05-Sep 60 MT  -> feasible
10-Sep 60 MT  -> feasible
12-Sep 60 MT  -> short/late unless another receipt is planned
```

Campaign creation does not require all campaign material to exist on day one.

---

## 7. Demand -> Production Order -> material requirements

### MTO

```text
SAP SO / item
 -> Production Order
 -> requirement snapshot
 -> BOM/material requirements
 -> manufacturing plan
```

### MTS

```text
stock target / replenishment requirement
 -> internal Production Order
 -> BOM/material requirements
 -> manufacturing plan
```

No fake Sales Order is required for MTS.

Every quantity must remain traceable through allocations.

---

## 8. Campaign planning begins only after material/manufacturing need is known

Campaign planning should not itself be the material-requirements engine.

Correct responsibility split:

### Material engine
Determines internal production requirement quantities and shortfalls through BOM + inventory/time-phased supply.

### Campaign optimizer
Groups compatible **internal production requirements** into economically/operationally good campaigns and heats.

This prevents double-netting inventory in both campaign logic and BOM logic.

---

## 9. Heat formation

Heat formation must answer:

- how much liquid steel/fresh cast output is internally required;
- which configured steelmaking routes can make it;
- which furnace capacity envelopes are feasible;
- number of heats;
- heat quantities;
- yield-adjusted input/output;
- grade/customer/process constraints;
- heat-to-PO/material-requirement allocation.

Heat size is not a random global number.

Every heat must be traceable back to material requirements and ultimately demand.

---

## 10. Manufacturing route

The route is master-data driven.

Examples may include:

```text
EAF -> LRF -> CCM
EAF -> LRF -> VD -> CCM
BOF -> LF -> CCM
Induction furnace -> LF -> CCM
other configured secondary metallurgy -> CCM
```

Downstream:

```text
CCM -> hot charge -> RM
CCM -> billet buffer -> RHF -> RM
known billet inventory -> RHF -> RM
RM -> TMT -> cooling -> cutting -> bundling
RM -> rod/coiling route
```

APS must not hard-code one meltshop topology.

---

## 11. Resource flexibility

For every constrained operation:

```text
Eligible Resources
 -> Planned Resource
 -> Commitment State
 -> Committed Resource
 -> Actual Resource
```

Frequency of use is irrelevant.

If LRF-2 can process one heat per year, it is still a valid eligible alternative for that heat and must remain available until commitment.

The same applies to:
- primary furnace;
- LRF/LF;
- VD/RH;
- CCM;
- RHF;
- RM;
- constrained finishing equipment.

Local redispatch must revalidate the same route, flow, thermal, material, sequence and resource constraints as the original solve.

---

## 12. Finite scheduling

Finite scheduling decides:

- eligible physical resource assignment;
- operation timing;
- per-resource sequence;
- setup/changeover;
- calendars/outages;
- capacity;
- predecessor/queue constraints;
- thermal constraints;
- material availability;
- stability/frozen constraints;
- service/tardiness objective.

Same-type equipment remains independent physical timelines.

Resource scheduling semantics must reflect physical behavior. Disjunctive `NoOverlap` is not universally valid for cumulative/residence resources such as some RHFs or cooling beds.

---

## 13. Plan Version

A Plan Version is the immutable explanation of one planning answer.

It must retain at least:

- demand/PO snapshot;
- customer/order requirements;
- BOM/material requirement tree;
- inventory/known-supply coverage;
- shortfalls;
- campaign decisions;
- heat structure and allocations;
- eligible resource alternatives;
- selected assignments;
- schedule;
- thermal/material assumptions;
- diagnostics;
- later dispatch revisions and comparison to parent plans.

---

## 14. Work Order generation

Work Orders are generated from the **solved manufacturing plan**, not directly from SO quantity.

Conceptually:

```text
Demand
 -> material/manufacturing requirements
 -> campaign/heat/route structure
 -> finite solved operations
 -> release mapping
 -> Work Orders + operation rows
```

WO allocation preserves PO/SO lineage.

A coarse MES Work Order does not erase APS process-operation detail.

---

## 15. Execution and actual material

Execution captures actual manufacturing truth:

- actual resource;
- actual start/end;
- actual processed quantity;
- heat/cast/strand output;
- billet lots;
- rolling input/output;
- bundles/coils/FG lots;
- quality/hold state.

Actual material output enters inventory exactly once.

Physical genealogy is separate from commercial demand allocation.

---

## 16. Replanning

Replan inputs are refreshed from:

- completed operations;
- running operations;
- released/committed operations;
- actual material receipts;
- remaining committed future receipts;
- current inventory;
- current resource state;
- remaining demand.

Rules:

- completed work is fixed/history;
- running work retains actual machine/start;
- already-produced quantity is inventory;
- only remaining committed production stays as future supply;
- no duplicate replacement production is generated for material already on the way;
- uncommitted future work may be reoptimized;
- local repair is preferred when a small operational change occurs.

---

## 17. End-to-end acceptance scenarios

### Scenario A — FG stock covers demand
No manufacturing WOs.

### Scenario B — billet stock covers rolling
No SMS heats; RM/downstream scheduled.

### Scenario C — billet missing but internally manufacturable
SMS/CCM production is planned to create billet before rolling need.

### Scenario D — future committed billet
Rolling waits for the future receipt; no duplicate heat.

### Scenario E — recursive raw-material requirement
Finished demand explodes through billet/liquid steel/hot metal/burden/ore as configured. Inventory is netted at each level. A non-manufacturable uncovered leaf remains a shortfall.

### Scenario F — SMS unavailable, known billet exists
RM may continue.

### Scenario G — SMS unavailable, no billet supply
Rolling need remains visible with shortfall; APS does not invent supply.

### Scenario H — rare alternate LRF
Heat can be redispatched to a qualified alternate LRF even if that resource is seldom used.

### Scenario I — partial heat actual
Actual quantity + remaining committed future quantity equals the correct remaining supply; no double counting.

### Scenario J — month-long progressive supply
Later campaign operations consume material made later in the horizon, not only opening inventory.

---

## 18. Remaining cross-cutting gaps

This end-to-end flow depends on completion of at least:

- #9 thermal model
- #14 time-phased material engine
- #15 campaign optimization
- #16 resource late binding / redispatch
- #18 physical genealogy
- #19 diagnostics
- #33 recursive BOM
- #34 generic steel route projection
- #35 resource scheduling modes
- #36 backend visibility
- #38 canonical pipeline cleanup
- #39 master-data wiring
- #42 rule consistency
- #44 end-to-end closure

Subsystem completion is not sufficient. The flow must work as one canonical .NET pipeline.