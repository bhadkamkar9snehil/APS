# APS End-to-End Manufacturing Planning Flow

**Status:** canonical manufacturing-planning causal flow  
**Re-baselined:** 23-Aug-2026 against integrated `main`  
**Scope:** domain, planning, persistence, release, execution and replan semantics. UI presentation is covered separately.

APS is a **manufacturing planning and scheduling system**. It is not a procurement recommendation engine and not a logistics-transfer recommendation engine.

APS consumes authoritative customer/stock demand, qualified inventory, known incoming material, committed/released internal WIP, resource/master state and execution actuals. It may create upstream **internal manufacturing requirements** where the configured plant knows how to make the required material. If a requirement cannot be covered by qualified supply or internal manufacturing in time, APS keeps the requirement visible as an explicit shortfall/late-supply condition.

APS never deletes demand merely because material is unavailable today.

---

## 1. Canonical causal chain

```text
SAP Sales Order item / MTS stock requirement
        |
        v
Normalize customer / grade / product / section / route requirements
        |
        v
Qualified finished-goods coverage
        |
        +--> fully covered -> customer demand remains visible; no unnecessary MTO manufacture
        |
        v
Uncovered finished-product manufacturing requirement
        |
        +--> MTO -> derived/reconciled Production Order with SO-item lineage
        +--> MTS -> stock-policy Production Order
        |
        v
Recursive BOM / material-requirement graph
        |
        v
Time-phased material coverage at each requirement node
        |
        +--> qualified on-hand inventory
        +--> authoritative known incoming material
        +--> released/running internal production receipts
        +--> APS-planned internal production receipts
        |
        v
Uncovered material quantity at required time
        |
        +--> internally manufacturable in configured plant?
        |       |
        |       +--> YES -> create upstream internal production requirement
        |       |           and recursively resolve its own material needs
        |       |
        |       +--> NO  -> Shortfall / NotManufacturableHere
        |
        v
Schedulable internal production requirements
        |
        v
Campaign candidate optimization
        |
        v
Grade sequence + furnace-feasible heat structure
        |
        v
Configured ManufacturingRoute operations
        |
        v
Finite resource / material / thermal schedule
        |
        v
Immutable persisted Plan Version
        |
        v
Readiness review -> Approved
        |
        v
Identity-only persisted release
        |
        v
Work Orders + process operations
        |
        v
Execution actuals + actual material transformation/genealogy
        |
        v
Inventory / WIP / remaining-demand refresh
        |
        v
Bounded local repair or broader replan -> child Plan Version
```

Demand/material requirement is causality. Campaign is manufacturing aggregation/optimization. Resource assignment is a planning/dispatch decision. Work Orders are downstream execution artifacts. Actual material closes the loop.

---

## 2. Demand and Production Order semantics

### Sales Order item

A Sales Order item is customer/commercial demand. It retains customer, material/product, grade/specification, section/product form, open quantity, customer-required/confirmed date and applicable customer/order restrictions.

### MTO Production Order

A new MTO Production Order represents the **finished-product quantity that still must be manufactured internally after qualified FG coverage**.

Example:

```text
SO open quantity       100 MT
qualified FG coverage   30 MT
-----------------------------
MTO manufacturing PO    70 MT
```

Missing billet/raw material does not reduce the 70 MT PO. Upstream material planning explains how much can be supplied internally and what remains short/late.

### MTS Production Order

MTS demand originates from stock policy/replenishment need and does not require a fake Sales Order.

### Aggregation boundary

PO remains the demand/manufacturing lineage unit. Campaign/Heat/Rolling/route-operation/WO allocation records preserve PO/SO quantity/date/customer identity when physical production is shared.

---

## 3. Material planning states

A material requirement may resolve conceptually to the following states.

### CoveredNow

Qualified material is available in authoritative inventory at or before need time.

### CoveredByKnownReceipt

Material is not available now but a trustworthy receipt exists before the need time, such as:

- released/running internal output;
- authoritative incoming/in-transit material;
- another committed future receipt from the integration/state model.

APS consumes the known supply fact. It does not decide that the material should be purchased or transferred.

### PlannedInternalProduction

The material is absent now, but the configured BOM/route/master data show that the plant can make it internally. APS creates the upstream manufacturing requirement and schedules the producing operations.

### LateInternalSupply / LateSupply

The internal or known-supply path exists but cannot make the material available by the consuming need time under current finite constraints.

### Shortfall

The requirement remains uncovered after qualified inventory, known receipts, committed internal supply and feasible APS-planned internal production.

### NotManufacturableHere

The installation has no configured internal manufacturing path for the material. The requirement remains explicit; another business/system may decide how to respond. APS does not invent BUY/TRANSFER/supplier recommendations.

---

## 4. Recursive BOM is material causality, not mandatory finite scheduling depth

The configured BOM may extend beyond the part of the plant that APS finite-schedules.

Example material depth:

```text
finished bar/coil
 -> rolled intermediate
 -> billet/bloom
 -> liquid steel
 -> charge/raw materials
 -> upstream intermediate/raw-material requirements
```

At every node APS may net qualified supply and only recurse for the uncovered quantity.

The finite-scheduling scope can be narrower, for example:

```text
configured steelmaking/refining -> CCM -> optional RHF -> rolling -> finishing
```

A leaf raw-material requirement may therefore appear as a time-phased shortfall without a corresponding APS Work Order if its producing process is outside the configured scheduling scope.

If a future installation configures an upstream producing stage as an APS-managed ManufacturingRoute, the same material graph can create an internal manufacturing requirement for it without redesigning the BOM model.

---

## 5. Time-phased material availability is fundamental

APS does **not** plan only against opening inventory.

At a requirement node the conceptual netting is:

```text
Gross requirement
 - qualified on-hand supply available by need time
 - authoritative known incoming supply available by need time
 - committed/released internal future supply available by need time
 - already planned internal supply available by need time
 = uncovered requirement
```

The exact production implementation may represent reservations/coverage with more detailed persisted facts, but the causal rule is the same.

### Month-long example

```text
01-Sep opening billet          0 MT
04-Sep internal cast receipt  65 MT
09-Sep internal cast receipt  65 MT
15-Sep internal cast receipt  65 MT

Rolling need:
05-Sep 60 MT -> first receipt can satisfy it
10-Sep 60 MT -> second receipt can satisfy it
16-Sep 60 MT -> third receipt can satisfy it
```

A month-long Campaign does not require all 180 MT to exist on 01-Sep.

If a required receipt arrives after the consumer need time, APS should expose lateness/shortfall and finite-schedule consequences rather than silently treating the future material as available early.

---

## 6. Material netting prevents unnecessary upstream manufacture

### FG covers customer demand

No new manufacturing is created for the covered quantity.

### FG absent, qualified billet available

Plan rolling/downstream work from billet without creating unnecessary SMS heats.

### Billet absent, internally manufacturable

Create the upstream steelmaking/casting requirement and schedule it before downstream consumption where feasible.

### SMS unavailable, authoritative billet receipt exists

Use the qualified future billet receipt if it arrives in time; do not generate a replacement internal heat solely because opening billet inventory is zero.

### SMS unavailable and no usable billet supply

Keep the rolling/customer manufacturing requirement visible and report the billet/material shortfall. Do not fabricate a procurement action.

---

## 7. ManufacturingRoute is authoritative process topology

APS does not assume one fixed steel chain such as:

```text
EAF -> LRF -> VD -> CCM -> RHF -> RM
```

The configured `ManufacturingRoute` determines which process operations exist, their order and the applicable input/output/queue/decoupling semantics.

Examples of valid configured downstream paths include:

```text
CCM -> HotRoll
CCM -> Reheat -> HotRoll
CCM -> HotRoll -> ColdRoll -> Finish
CCM -> HotRoll -> Reheat -> HotRoll
billet inventory -> Reheat -> HotRoll
```

Pre-CCM routes likewise may include the configured primary steelmaking/refining/treatment operations appropriate to the plant and grade.

### Conditional operations

VD, reheating and other treatment operations may be required, optional or forbidden by the effective route/grade/order requirement. Their presence is not inferred from a universal plant diagram.

---

## 8. Hot charge, reheating and inventory decoupling

Hot charge is a preferred valid route only when:

- the configured route allows it;
- the material is fresh/known-hot with adequate thermal evidence;
- grade/order policy permits it;
- a valid physical hot-transfer path exists;
- downstream timing/resource feasibility preserves the thermal window.

Reheating becomes required when, for example:

- billet is cold/yard material;
- the route/order explicitly requires reheating;
- direct hot charge is prohibited;
- measured/estimated thermal state falls outside the downstream entry requirement;
- an inventory/decoupling point intentionally breaks guaranteed hot continuity.

If reheat is required and no eligible configured reheat path exists, APS reports infeasibility. It does not invent a furnace or bypass the requirement.

A downstream outage does not automatically erase valid upstream billet production where a legitimate inventory decoupling path exists. The produced intermediate may remain planned/buffered and be reevaluated later from actual material/thermal state.

---

## 9. Campaign, grade sequence and heat planning

Campaign formation is optimization, not demand creation and not authoritative sort-and-fill.

Candidate selection may account for:

- PO allocation-level service obligations;
- route/section/caster compatibility;
- grade/sequence compatibility and forbidden transitions;
- transition time/penalty;
- customer/quality segregation;
- furnace-feasible heat envelopes;
- heat utilization/residual economics;
- downstream route/resource feasibility;
- MTO/MTS policy;
- setup/campaign economics;
- early-production/service cost;
- stability against the persisted baseline during replan.

The selected Campaign may aggregate multiple Production Orders while each allocation preserves its own quantity/date/customer lineage.

---

## 10. Quantity-aware service dates

Shared physical work does not collapse several customer service dates into one artificial due date.

Example:

```text
PO-A 40 MT due 10-Sep
PO-B 60 MT due 18-Sep
shared campaign/physical production
```

Campaign summary may expose earliest/latest dates, but allocation-level service obligations remain distinct. Finite scheduling/service evaluation uses the relevant quantity/date/priority obligations rather than pretending all 100 MT has the same customer due date.

`ProductionRequiredByUtc` should reflect the applicable configured post-production/quality/packing/dispatch allowance; where no effective allowance exists, the service-date basis falls back explicitly rather than being silently invented in UI code.

---

## 11. Resource eligibility and operational flexibility

Operation identity comes from the configured manufacturing requirement. Resource assignment is separate:

```text
operation requirement
 -> eligible physical resources
 -> planned resource
 -> commitment state
 -> committed resource
 -> actual resource
```

Parallel same-type resources retain independent physical timelines.

An LRF-ready heat may, for example, remain technically eligible for more than one CCM before the commitment boundary. If the planned caster becomes unavailable, a bounded redispatch to another still-valid CCM should preserve Heat/Campaign/PO/SO identity while revalidating route, material, thermal, transfer, queue, sequence, calendar and capacity constraints.

The complete generic planned→committed→actual redispatch lifecycle is the current #16 work area.

---

## 12. Finite scheduling

Finite scheduling assigns eligible physical resources and times while enforcing the applicable configured constraints, including:

- physical resource capacity/scheduling mode;
- resource calendars/operating state/derating;
- route and material dependencies;
- transfer/queue windows;
- liquid/billet thermal feasibility where modeled;
- resource transition/setup sequence;
- service obligations;
- time fences and plan stability;
- operating-state/scenario overlays.

Unary and cumulative resources are distinct scheduling semantics. A physical CCM/RM/RHF/etc. is not merged into a type-level artificial queue.

---

## 13. Plan Version, approval and release

Every canonical production calculation/replan persists immutable Plan Version truth.

Current lifecycle includes:

```text
Draft -> Feasible -> Approved -> Released
```

with failed/superseded states where applicable.

A historical Plan Version should be read using its persisted snapshots/assumptions rather than silently applying today's mutable resource/calendar masters.

Release is identity-only:

```text
PlanVersionId
 -> persisted plan snapshots
 -> readiness/approval policy
 -> persisted release service
 -> Work Orders + ScheduledOperations
```

The client does not reconstruct campaigns/routes/schedules and submit a competing release payload.

Readiness currently includes persisted material/supply evidence and MTO service-completion evidence.

---

## 14. Execution and replanning

Execution adds actual facts; it does not rewrite historical plan truth.

Canonical actuals include, as applicable:

- work-order/operation status;
- actual resource;
- actual start/end;
- actual quantity;
- heat/cast/strand identity;
- produced/consumed material lots/units;
- correction/provenance history.

Commercial allocation lineage and physical material genealogy remain distinct relationships.

Replanning consumes:

- persisted baseline Plan Version;
- completed/running/protected operations;
- actual material/inventory state;
- committed/released future internal output;
- current authoritative masters/scenario state;
- applicable time-fence/resource-override policy.

The same Production-mode planning kernel creates a child Plan Version for the remaining work.

---

## 15. Scenarios, CTP and capacity

Scenario/CTP/capacity work must reuse the same canonical demand/material/route/resource semantics as normal planning rather than creating sidecar planning logic.

Rough-cut capacity and finite scheduled occupancy are different truths and must remain explicitly labeled/separate.

Resource outage scenarios do not permit fabricated material supply. Known billet/inventory may still support downstream production while uncovered material remains short/late.

---

## 16. Core acceptance invariants

APS must preserve at least these invariants end-to-end:

1. Fully FG-covered SO demand creates no unnecessary manufacturing PO.
2. Partial FG coverage creates only the uncovered manufacturing quantity.
3. Missing current billet/raw material does not suppress internally required finished manufacture.
4. Future known/planned material can satisfy later campaign operations when available in time.
5. Deep BOM leaf shortfalls remain explicit and attributable.
6. SMS outage plus qualified billet supply can still allow rolling.
7. SMS outage plus no billet supply leaves explicit material shortfall; APS does not invent supply.
8. Same-type parallel resources remain independent physical timelines.
9. Cumulative/shared resources use configured cumulative capacity rather than universal `NoOverlap`.
10. PO/customer service dates survive Campaign/Heat/Rolling aggregation at allocation grain.
11. Actual production and remaining future supply do not double-count material.
12. Physical genealogy and commercial lineage remain separately traversable.
13. Month-long plans can consume progressively produced material.
14. Infeasibility reports named domain cause/evidence rather than only `Infeasible`.
15. Scenario/CTP semantics do not diverge from the canonical production kernel.
16. Billet thermal aging can change hot-charge/reheat decisions without erasing the billet requirement/material.
17. Configured downstream routes are not forced through a first-HotRoll architecture pivot.
18. Alternate-resource redispatch preserves operation/business identity and revalidates all applicable constraints.
19. Historical Plan Versions remain immutable/explainable from persisted evidence.
20. Release cannot bypass persisted approval/readiness rules.

Executable coverage and remaining integrated gaps are maintained in [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md).

---

## 17. Documents governing this flow

- [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md) — current implementation-state authority;
- [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md) — remaining implementation sequence;
- [`APS_Demand_to_Production_Order_and_Due_Date_Model.md`](APS_Demand_to_Production_Order_and_Due_Date_Model.md) — demand/service semantics;
- [`APS_Backend_Visibility_Contract.md`](APS_Backend_Visibility_Contract.md) — typed exposure target;
- [`APS_Steel_Domain_Architecture_Roadmap.md`](APS_Steel_Domain_Architecture_Roadmap.md) — current steel-domain architecture/roadmap;
- [`APS_Backend_Canonical_Path_Inventory.md`](APS_Backend_Canonical_Path_Inventory.md) — production lifecycle authority;
- [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md) — acceptance strategy;
- [`windows-ci.md`](windows-ci.md) — authoritative Windows verification contract.

Current code on `main` wins over a stale historical document when implementation-state claims conflict.
