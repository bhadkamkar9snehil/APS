# APS Backend Work Program

**Status:** Canonical backend implementation program  
**Scope:** Backend/domain/planning first. Production UI depends on authoritative backend truth/read models.  
**Canonical integrated branch:** `main`  
**Verification rule:** **Do not use GitHub Actions or CI for APS project verification.** Verification is performed later in the intended development environment.

Canonical companions:

- `APS_Backend_Acceptance_Audit_2026-08-18.md`
- `APS_End_to_End_Manufacturing_Planning_Flow.md`
- `APS_Demand_to_Production_Order_and_Due_Date_Model.md`
- `APS_Backend_Canonical_Path_Inventory.md`
- `APS_Backend_Visibility_Contract.md`
- GitHub #2, #37, #44, #47 and #49

---

## 1. Product boundary

APS is a **manufacturing planning and scheduling system**.

It consumes authoritative facts about demand, qualified inventory, known incoming material, committed/released internal WIP, APS-planned internal production, resource state and execution actuals.

For uncovered material:

```text
qualified supply exists by need time?
  yes -> reserve/use it
  no  -> internally manufacturable in configured plant?
            yes -> create upstream internal production requirement
            no  -> Shortfall / NotManufacturableHere
```

APS does **not** recommend procurement or inter-plant transfer actions. Purchased/transferred material already present in authoritative inventory/incoming integration is simply known supply.

---

## 2. Canonical causal flow

```text
SAP SO item / MTS requirement
 -> normalize customer + grade + product requirements
 -> qualified FG coverage
 -> MTO/MTS Production Order manufacturing requirement
 -> recursive BOM/material requirement graph
 -> time-phased material coverage/reservation
 -> internally manufacturable uncovered requirement
 -> Campaign candidate optimization
 -> grade sequence + furnace-feasible heats
 -> configured ManufacturingRoute operations
 -> finite resource/material/thermal schedule
 -> immutable Plan Version
 -> Work Orders + process operations
 -> execution actuals + physical material transformation
 -> inventory/WIP/remaining-demand refresh
 -> bounded local repair or broader replan
```

**Demand/material requirement is causality. Campaign is aggregation/optimization. Resource assignment is a plan/dispatch decision. Work Order is execution. Actual material closes the loop.**

The route is configuration, not a hard-coded plant diagram. Equipment names such as EAF/LRF/VD/CCM/RHF/RM do not imply that every plant or grade uses every stage.

---

## 3. Operational flexibility principles

### 3.1 Process requirement versus resource assignment

Operation identity comes from route/grade/order requirements. Physical resource selection remains flexible until it is operationally committed.

```text
operation requirement
 -> eligible physical resources
 -> planned resource
 -> committed resource
 -> actual resource
```

### 3.2 VD is conditional

VD is included when grade/order rules require it, skipped when optional and unnecessary, and rejected when forbidden. There is no universal VD stage.

### 3.3 Hot rolling and reheating are conditional

Hot charge is preferred when billet is still thermally eligible and a valid hot path is available because it avoids reheating energy, time and capacity.

Reheating is used when billet is cold/yard material, hot eligibility has expired, or grade/order policy requires heating. APS does not assume `CCM -> RHF -> RM` as a fixed topology.

### 3.4 Downstream outage does not erase upstream billet demand

Where the plant has a legitimate inventory decoupling point, a rolling-mill outage does not automatically invalidate otherwise-valid upstream steelmaking/casting.

```text
steelmaking / casting continues when operationally required
        -> billet produced
        -> mill unavailable
        -> billet buffered / yard inventory
        -> mill returns
        -> choose valid feed using actual state
              fresh/still hot -> direct rolling where feasible
              yard/cold       -> configured reheating path
```

#56 owns billet thermal-state fidelity. #16 owns late resource commitment and operational redispatch. #18 closes execution/material actuals.

---

## 4. Engineering issue standard

Before implementation, every primary backend issue states:

1. current implementation state;
2. exact remaining gap;
3. target domain behavior;
4. canonical owner — evolve existing abstractions, avoid sidecars;
5. inputs/masters/controls;
6. outputs/persisted facts;
7. solver/material/execution interaction where applicable;
8. visibility/read-model requirements;
9. dependencies/blockers;
10. non-goals/product boundary;
11. compatibility behavior;
12. acceptance scenarios;
13. completion evidence required;
14. no-GitHub-Actions/CI process rule.

A feature closes only when its applicable chain is coherent:

```text
Domain/master
 -> SQL persistence
 -> authoritative provider/import
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version audit
 -> execution/replan where relevant
 -> read model/API
```

A class existing is never sufficient evidence of completion.

---

## 5. One-primary-issue-at-a-time discipline

For each active primary issue:

1. audit the current `main` call path and any newer unintegrated WIP branch;
2. confirm target semantics and direct dependencies;
3. fix the canonical root abstraction;
4. complete all applicable layers;
5. satisfy applicable #39/#40/#41/#42/#36 cross-cutting requirements while touching that path;
6. add focused acceptance/regression tests without using GitHub Actions/CI;
7. record concrete implementation evidence in the issue;
8. close only when acceptance is genuinely satisfied;
9. integrate the completed tranche to `main`;
10. then start the next primary issue.

Do not run several major domain redesigns in parallel.

---

# 6. Ordered implementation program

## Phase 0 — repository authority — COMPLETE

### #46 Repository documentation cleanup/archive — closed

Canonical/current/reference/archive document authority established. The legacy Python/workbook implementation was subsequently retired from the active tree in v0.2.6 and remains available at tag `v0.2.5` only.

---

## Phase 1 — canonical boundaries and demand — COMPLETE

### #38 One authoritative production path — closed

One production lifecycle owns demand, planning, persistence, readback, release and replan. Demo/reference paths are explicitly separated.

### #45 SO item -> FG coverage -> MTO Production Order — closed

```text
SO open demand
- qualified FG coverage/reservation
= MTO finished-product manufacturing requirement
```

Allocation-level customer service dates survive later aggregation.

---

## Phase 2 — material requirements — COMPLETE FOUNDATIONS

### #33 Recursive BOM/material-requirement graph — closed

Canonical .NET recursive material causality with lineage/version/UOM semantics.

### #14 One time-phased material ledger/reservation engine — closed

Material absent today may still satisfy a requirement later through known/committed/planned internal supply. Planning is not restricted to current stock.

### #11 Billet/known-incoming contingency — closed foundation

Known billet can feed downstream production without unnecessary upstream make decisions; absent qualified supply remains explicit shortfall rather than invented supply.

---

## Phase 3 — Campaigns, routes, thermal state and finite flexibility

### #15 Campaign/grade-sequence/heat candidate optimization — closed and integrated

Campaign formation uses candidate selection, service obligations, transition economics, furnace-feasible heats, downstream feasibility, MTO/MTS behavior and replan stability rather than production-authoritative sort-and-fill.

### #34 Route-driven pre-CCM topology — closed foundation

Configured ManufacturingRoute controls steelmaking/refining/casting operation order and presence. No fixed EAF/LRF/VD chain is assumed.

### #58 Route-driven downstream projection without first-HotRoll pivot — closed and integrated

Every configured post-CCM operation, including first HotRoll, is projected through one route-driven mechanism. Direct hot charge, billet-only routes, arbitrary pre-roll operations, inter-pass reheating and multi-mill routes remain valid configurations.

### #9 Liquid-steel thermal envelope/resource-pair constraints — closed foundation

Liquid-steel transfer/superheat/casting-temperature feasibility is configuration-driven through CCM.

### #56 Billet thermal chain, hot-charge eligibility and replan thermal actuals — **CURRENT PRIMARY ISSUE**

Add time/temperature-aware billet state so planned/actual transfer/wait/buffer/yard conditions determine whether billet remains hot-charge eligible or requires configured reheating.

The issue must preserve inventory decoupling: a downstream mill outage does not automatically cancel valid upstream billet production.

### #35 Resource scheduling modes/cumulative capacity — closed foundation

Physical resource occupancy is master-driven (disjunctive/cumulative) rather than universal `NoOverlap`.

### #16 Late-bound resource assignment, commitment and operational redispatch

Complete generic lifecycle:

```text
Eligible Resources -> Planned Resource -> Commitment State -> Committed Resource -> Actual Resource
```

Alternatives survive the initial solve until policy/actual state commits them. Local repair revalidates material, route, thermal, flow, calendar, sequence and resource constraints.

### #17 Operating-state scenarios/outages — closed foundation

Outages/derating/restrictions are effective plant-state overlays consumed by the same canonical planner.

---

## Phase 4 — execution closure and explanation

### #18 Full execution/material genealogy

Close actual transformation and actual-state feedback:

```text
heat operations -> cast/strand -> billet/bloom -> heating/rolling
 -> rolled intermediate -> finishing -> bundle/coil/FG
```

Commercial lineage remains separate from physical genealogy.

### #19 Planner-grade diagnostics

Normalize domain causes across validation, material, Campaign, route, resource, thermal, capacity, sequence, stability and execution. Provide advisory restoration/minimum-relaxation evidence without weakening hard rules.

---

## Phase 5 — scenario/decision services and complete exposure

### #57 Scenario material contingency + richer Plan Version comparison

Prove that scenario resource changes propagate through material availability/shortfall and compare service/material/campaign/capacity/diagnostic effects between Plan Versions.

### #43 CTP/scenario/capacity convergence

CTP, scenario planning and capacity analysis use the same canonical demand/material/route/resource semantics as normal planning. Rough-cut capacity remains distinct from finite scheduled occupancy.

### #36 Complete backend read/command surface

Every meaningful planning fact/decision/lever has an intentional typed contract. UI must not recalculate material balance, infer resource alternatives or reconstruct route/diagnostic truth client-side.

---

## Phase 6 — configuration/reference acceptance readiness

### #60 Validated operational master authoring

Complete authoritative write/validation workflows for thermal, scenario and resource-scheduling masters using canonical persistence.

### #61 Deterministic integrated-steel reference dataset

Persist a realistic planning-density reference dataset that exercises the supported process taxonomy/topology without inventing unsupported plant facts.

### #44 Final end-to-end manufacturing-planning acceptance gate

Parent epics close only after the canonical .NET lifecycle satisfies the final scenarios.

### Scope-gated #62 Process taxonomy

Add non-EAF/secondary-metallurgy process identities only when evidenced by the target/reference plant data. Taxonomy must not become hard-coded topology.

### Independent #59 Tailwind build verification

Portable pinned build implementation exists; clean OS verification remains independent of the backend planning sequence.

---

# 7. Cross-cutting gates

These are completed incrementally inside the active primary issue.

## #39 Master-data wiring

Maintain `Domain -> EF/SQL -> provider -> planner -> PlanVersion -> read API` for every planning-affecting master.

## #40 Standard logging

Use structured `ILogger<T>` production logging. Runtime logs complement but never replace Plan Version audit.

## #41 Validation

Application/master boundaries use centralized validation; domain services own business invariants; solver owns finite feasibility.

## #42 Effective rule consistency

Campaign, route, solver, scenario and redispatch consume one effective capability/transition interpretation.

## #32 Operational/material fidelity tracker

Not an implementation owner. Closes only after material, redispatch, execution and readback prove the required invariants.

## #44 Final end-to-end gate

Final release-readiness proof for the canonical backend loop.

---

# 8. Current acceptance matrix

| Issue | Concern | Current state | Required evidence before closure |
|---|---|---|---|
| #46 | Repository/document authority | **Closed** | canonical/reference/archive authority |
| #38 | Canonical production path | **Closed** | one production lifecycle + explicit demo boundary |
| #45 | MTO demand orchestration | **Closed** | SO coverage -> PO + service-date trace |
| #33 | Recursive BOM | **Closed foundation** | recursive requirement lineage + coverage/shortfall |
| #14 | Time-phased material | **Closed foundation** | reservations/receipts/consumption/projected availability |
| #11 | Billet/known incoming | **Closed foundation** | qualified supply allocation and shortfall behavior |
| #15 | Campaign optimization | **Closed** | candidates/objective/heat structure/PlanVersion evidence |
| #34 | Pre-CCM route topology | **Closed foundation** | route-driven operations and resource candidates |
| #58 | Downstream route topology | **Closed** | no first-HotRoll pivot; route/read/release regressions |
| #9 | Liquid-steel thermal | **Closed foundation** | liquid thermal resource-pair feasibility |
| #56 | Billet thermal state | **Current** | time/temperature hot-vs-reheat decision + actual-state readback |
| #35 | Resource modes | **Closed foundation** | physical occupancy semantics |
| #16 | Late resource binding | Open | eligible/planned/committed/actual + local redispatch history |
| #17 | Scenario resource state | **Closed foundation** | effective plant-state overlay |
| #18 | Execution/genealogy | Open | operation actuals + recursive physical genealogy |
| #19 | Diagnostics | Open | stable causes/objective/advisory restoration |
| #57 | Scenario material comparison | Open | material/service/campaign/capacity/diagnostic comparison |
| #43 | CTP/scenario/capacity convergence | Open | typed decision services on canonical kernel |
| #36 | Backend visibility | Open | complete typed read/command inventory |
| #60 | Master authoring | Open | validated persisted operational controls |
| #61 | Reference dataset | Open | deterministic realistic integrated dataset |
| #44 | End-to-end acceptance | Open | complete canonical acceptance scenarios |

---

# 9. Planning controls/levers rule

Every planning-affecting control must be **enforced** or explicitly non-planning. Major families include:

- SO/MTS priority and service-date policy;
- customer/segregation requirements;
- grade chemistry/process/VD/thermal requirements;
- route required/optional/forbidden operations;
- resource capability/preference/prohibition;
- resource state/calendar/derating;
- Campaign min/target/max and mixing policy;
- furnace/heat capacity envelopes and yields;
- grade/section/product transitions;
- CCM sequence/tundish/strand rules;
- billet hot-charge/reheat thresholds;
- resource scheduling mode/capacity;
- time fences/stability penalties;
- assignment/commitment policy;
- scenario overrides;
- controlled solver objective/time-limit policies.

A configurable property silently ignored by the planner is unacceptable.

---

# 10. Branch/integration rule

`main` is the canonical integrated snapshot.

The immediate pre-Claude .NET planning-core branch was `agent/aps-dotnet-planning-core`; Claude's `claude/project-status-review-o2dx1j` was 11 commits ahead and 0 behind it. Both histories are now contained in `main` and are not separate product authorities.

Legacy Python/UI histories must not be history-merged wholesale into the .NET product. Retain at most one audited tip per genuinely divergent legacy lineage while useful behavior is inspected/ported deliberately; then retire those branch refs.

Completed primary issue branches are redundant after their commits are contained in `main`.

---

# 11. UI readiness gate

Dependent production UI ships only when backend truth is queryable without client-side reconstruction. UI design may proceed, but missing backend planning behavior must not be implemented in the client.

---

# 12. Verification rule

**Do not use GitHub Actions or CI for APS project verification.**

During implementation:

- write focused unit/integration/acceptance tests;
- perform source-level review and record expected verification;
- run build/test/runtime checks later in the intended developer environment;
- never claim a branch is green without that verification.

---

# 13. Final backend readiness definition

The backend is ready for complete production UI/release only when:

1. #44 scenarios work through the canonical .NET path;
2. production never silently uses demo/default masters;
3. recursive material planning and finite scheduling share one material truth;
4. customer/service obligations survive aggregation;
5. route and conditional process semantics survive without fixed topology assumptions;
6. eligible resources and physical parallelism survive through execution;
7. upstream/downstream inventory decoupling behaves correctly during disruptions;
8. actual production closes genealogy/replan without double counting;
9. diagnostics explain failures and major decisions;
10. all meaningful facts/levers are typed/queryable;
11. master/logging/validation/rule-consistency gates are satisfied;
12. repository authority is clear and stale branches/docs are retired once their unique evidence is preserved.
