# APS Backend Work Program

**Status:** Canonical backend implementation program  
**Scope:** Backend/domain/planning only. UI implementation is deferred until backend visibility and end-to-end acceptance are complete.  
**Process rule:** **Do not use GitHub Actions or CI for APS project verification.** Verification will be performed later in the intended development environment.

This document converts the backend acceptance audit into one dependency-ordered implementation program. It exists to prevent parallel half-implementations, ambiguous ownership and premature issue closure.

Canonical companion references:

- `APS_Backend_Acceptance_Audit_2026-08-18.md`
- `APS_End_to_End_Manufacturing_Planning_Flow.md`
- `APS_Backend_Visibility_Contract.md`
- `APS_Backend_Audit_Remediation_Map.md`
- `APS_Demand_to_Production_Order_and_Due_Date_Model.md`
- GitHub Epic #2
- GitHub Epic #37
- GitHub Issue #44 — final end-to-end acceptance gate
- GitHub Issue #47 — one-issue-at-a-time governance
- GitHub Issue #48 — documentation quality gate
- GitHub Issue #49 — dependency/completion-evidence index

---

## 1. Product boundary

APS is a **manufacturing planning and scheduling system**.

It may consume authoritative facts about:

- customer demand / SAP Sales Orders;
- MTS stock targets;
- current qualified inventory;
- known incoming material from existing integrations;
- released/committed internal WIP and expected internal receipts;
- current resource state/calendars;
- actual execution/material output.

For an uncovered material requirement:

```text
covered by inventory / known receipt / committed internal supply?
        |
        +-- yes --> reserve/use that supply
        |
        +-- no --> internally manufacturable in configured plant?
                         |
                         +-- yes --> create upstream internal production requirement
                         |
                         +-- no  --> explicit Shortfall / NotManufacturableHere
```

APS does **not** recommend BUY or TRANSFER actions. Procurement and logistics decisions remain outside the product boundary.

---

## 2. Canonical end-to-end manufacturing flow

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
 -> Plan Version
 -> Work Orders + process operations
 -> execution actuals + physical material transformation
 -> inventory/WIP/remaining-demand refresh
 -> bounded local repair or broader replan
```

The fundamental causal chain is:

> **Demand causes material requirements. Material requirements cause internal production requirements. Campaigns organize those requirements. The solver assigns resources/times. WOs execute the solved plan. Actual material feeds the next replan.**

Campaign is an optimization/aggregation construct; it is not the original cause of production.

---

## 3. Implementation-status legend

All status summaries and issue completion comments should use this vocabulary.

| Status | Meaning |
|---|---|
| **Implemented and authoritative** | The production path uses it consistently through all applicable layers. |
| **Implemented but partial/inconsistent** | Working code exists, but one or more applicable layers or call paths disagree/omit it. |
| **Modeled but not fully wired** | Domain/contracts exist but production persistence/provider/planner/solver/read path is incomplete. |
| **Legacy/reference only** | Useful prototype/migration behavior exists but is not production authority. |
| **Missing** | Canonical .NET capability does not exist yet. |
| **Superseded** | Retained only for history/reference and must not drive new implementation. |

A domain class existing is **never** sufficient to call a capability implemented and authoritative.

---

## 4. Issue specification standard

Before implementation, every backend issue must state explicitly:

1. **Current implementation state** — what checked-in .NET code actually does today.
2. **Audit finding / gap** — the missing, contradictory, duplicate or unsafe behavior.
3. **Target domain behavior** — desired manufacturing/planning semantics.
4. **Canonical ownership** — which existing abstraction owns the behavior; avoid sidecars.
5. **Inputs / masters / controls** — all configurable planning inputs.
6. **Outputs / persisted facts** — including Plan Version facts where relevant.
7. **Solver/material/execution interaction** — if applicable.
8. **Visibility/read-model requirements** — what must be queryable later.
9. **Dependencies / blockers** — exact issue dependencies.
10. **Non-goals / scope boundary** — especially manufacturing-only material planning.
11. **Compatibility/migration considerations** — legacy/demo behavior where relevant.
12. **Acceptance scenarios** — domain scenarios, not only unit-level assertions.
13. **Completion evidence required** — concrete production paths required before closure.
14. **Process rule** — no GitHub Actions/CI.

---

## 5. Feature completion rule

A feature is complete only when its **applicable** production chain is coherent:

```text
Domain/master
 -> SQL persistence
 -> authoritative CRUD/import/provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version audit
 -> execution/replanning where relevant
 -> read model
 -> application/HTTP contract
```

If one layer genuinely does not apply, the issue must explicitly state why.

Examples:

- a BOM leaf raw-material requirement may have no finite-schedule operation; solver layer is intentionally not applicable;
- a static descriptive master may be referenced by stable version rather than copied into every Plan Version;
- a planning-only candidate may need Plan Version decision evidence but no execution entity.

---

## 6. One-issue-at-a-time implementation discipline

For each primary issue:

1. re-read current code and canonical issue specification;
2. identify direct dependencies only;
3. evolve the canonical abstraction at the root;
4. complete all applicable layers for that feature;
5. apply cross-cutting logging/validation/master/read rules while touching that path;
6. document migration/fallback behavior;
7. add focused acceptance/regression tests in code, but do not use GitHub Actions/CI;
8. update the issue with concrete implementation evidence;
9. close only after every acceptance scenario is satisfied;
10. then select the next primary issue.

Do not run several major domain redesigns in parallel merely because they are related.

---

# 7. Ordered backend implementation program

## Phase 0 — repository and documentation authority

### 1. Issue #46 — Repository cleanup / Current-Reference-Archive structure

**Purpose:** remove ambiguity about which documents and prototype implementations are authoritative before deeper code changes resume.

Required output:

- repository/document inventory;
- cleanup manifest;
- canonical/current/reference/archive classification;
- legacy Python/workbook explicitly marked migration/reference;
- root README/docs index updated;
- stale docs archived without deleting valuable history.

No backend behavior changes belong in this phase.

---

## Phase 1 — authoritative backend boundaries and demand

### 2. Issue #38 — One authoritative planning/query/execution path

Establish the canonical production call graph and isolate demo/legacy/fallback behavior.

Key result:

```text
one production planning orchestrator
one material engine
one route/structure path
one finite scheduler
one execution lifecycle
one Plan Version truth
```

Production mode must not silently use sample/default masters.

### 3. Issue #45 — SO item -> FG coverage -> MTO Production Order

Create the missing MTO demand-orchestration lifecycle.

Canonical quantity rule:

```text
SO open demand
- qualified FG coverage/reservation
= MTO finished-product manufacturing requirement
```

Important semantics:

- different SO items normally remain separate POs;
- component/raw-material shortage does not shrink the PO;
- aggregation begins later at Campaign/Heat/Rolling/WO allocations;
- customer required date and production-required-by date are preserved separately;
- allocation-level service dates survive shared manufacturing.

---

## Phase 2 — canonical material requirements

### 4. Issue #33 — Recursive BOM/material-requirement graph

Port/strengthen the useful legacy recursive BOM capability into the authoritative .NET backend.

Support arbitrary configured depth:

```text
FG
 -> billet/bloom
 -> liquid steel
 -> hot metal/DRI/scrap/alloys/fluxes
 -> burden
 -> ore/pellet/sinter/coke/coal/other leaf material
```

The BOM graph calculates requirements even when some upstream manufacturing processes are not finite-scheduled.

### 5. Issue #14 — One time-phased material ledger/reservation engine

This becomes the only authority for material coverage/reservations across demand, BOM, Campaigns, solver, execution and replan.

Core rule:

```text
ProjectedAvailable(material, location, time)
 = opening usable inventory
 + known incoming receipts
 + committed internal production receipts
 + APS-planned internal production receipts
 + actual receipts
 - reservations
 - planned/released consumption
 - configured reserve
```

A material not available today may still be valid for an operation days/weeks later.

### 6. Issue #11 — Billet/known-incoming/SMS-down contingency

Implement as a **specialized use case of #14**, not another supply engine.

Required behavior:

- billet stock/known incoming can feed RM without unnecessary SMS heats;
- SMS-down + qualified billet + RHF/RM available can still produce a plan;
- no billet and no feasible internal manufacture = shortfall;
- no procurement suggestion.

---

## Phase 3 — manufacturing aggregation, routes and finite scheduling

### 7. Issue #15 — Campaign / grade-sequence / heat candidate optimization

Replace production-authoritative ordered sort-and-fill with explicit candidate generation/selection.

Campaign optimization must consider:

- allocation-level customer service;
- due-date spread / early-production consequence;
- transition cost;
- Campaign/heat utilization;
- downstream feasibility;
- MTS fill/deviation;
- replan stability.

Campaign may aggregate POs, but each allocation retains quantity/date/customer identity.

### 8. Issue #34 — Route-driven manufacturing topology

ManufacturingRoute becomes authoritative for operation order/presence.

Support configured variants such as EAF/LF, EAF/LF/VD, BOF/LF, induction + secondary metallurgy, RH/VD/AOD/VOD as required, and downstream-only existing-billet routes.

Do not add plant-specific `if` chains.

### 9. Issue #9 — Thermal/superheat/transfer constraints

Complete the configuration-driven thermal envelope through liquid steel and billet hot/cold routing.

Separate:

- liquid steel superheat/casting constraints;
- billet hot-charge/RHF/rolling-entry temperature constraints.

Hard thermal constraints remain hard.

### 10. Issue #35 — Resource scheduling modes

Replace universal `NoOverlap` with master-driven scheduling semantics.

Initial modes:

- Disjunctive;
- Cumulative.

Use one CP-SAT engine. Avoid premature simulation/framework over-engineering.

### 11. Issue #16 — Late-binding resource assignment / commitment / redispatch

Complete generic lifecycle:

```text
Eligible -> Planned -> Firm/Committed -> Actual
```

Apply uniformly to EAF/LRF/VD/CCM/RHF/RM/downstream constrained operations.

Resource usage frequency does not affect eligibility. A rarely used LRF must remain available if technically qualified.

---

## Phase 4 — execution closure and explanation

### 12. Issue #18 — Full execution and physical genealogy

Close the real material transformation loop:

```text
heat operations
 -> cast/strand
 -> billet/bloom
 -> RHF/RM
 -> rolled intermediate
 -> TMT/cut
 -> bundle/coil/FG
```

Commercial lineage remains separate:

```text
SO -> PO -> Campaign/WO allocations
```

### 13. Issue #19 — Planner-grade diagnostics

Normalize validation, material, Campaign, route, thermal, capacity, sequence, time-fence and solver diagnostics.

Provide domain causes and advisory restoration evidence rather than only `Infeasible`.

Do not silently relax hard metallurgy/customer/quality constraints.

---

## Phase 5 — decision services and complete backend exposure

### 14. Issue #43 — CTP / scenario / capacity convergence

CTP, scenarios and capacity analysis must use the same canonical demand/material/route/resource semantics as normal planning.

Keep rough-cut capacity separate from finite scheduled occupancy.

### 15. Issue #36 — Complete backend read/command visibility surface

Every meaningful fact and lever must be queryable before dependent UI implementation begins.

No UI should need to:

- read EF tables directly;
- parse opaque JSON for core facts;
- recompute BOM/material balance;
- recalculate service dates;
- infer resource alternatives;
- reproduce diagnostics;
- reconstruct genealogy.

---

# 8. Cross-cutting completion gates

These are **not parallel feature programs**. They are satisfied incrementally while implementing each primary issue.

## Issue #39 — Master-data wiring

Every active feature must complete applicable:

`Domain -> EF/SQL -> provider -> planner -> Plan Version -> read API`.

Maintain a Master Data Completeness Matrix.

## Issue #40 — Standard logging

Use `ILogger<T>` in production code and Serilog only as the host provider. Add structured lifecycle/correlation logging to paths touched by active work.

Do not create a custom logger.

## Issue #41 — Validation

Use FluentValidation for application/master boundary validation; domain services own true business invariants; solver owns finite feasibility.

## Issue #42 — Effective rule consistency

Campaign, route, solver, scenario and redispatch consume the same resolved capability/transition semantics.

No declared planning lever may be silently ignored.

## Issue #32 — Operational/material fidelity tracker

Closes only when #14 + #16 + execution/replan/readback demonstrate the required behavior. It is not an implementation owner.

## Issue #44 — Final end-to-end gate

No parent epic closes until its scenarios are demonstrated through the canonical .NET path.

---

# 9. Issue-by-issue acceptance matrix

| Issue | Primary owner / concern | Current audit status | Key upstream dependencies | Key downstream consumers | Required historical/read evidence before closure |
|---|---|---|---|---|---|
| #46 | Repository/document authority | Missing cleanup pass | none | all work | cleanup manifest, canonical/reference/archive index |
| #38 | Canonical production path | Partial/inconsistent | #46 | all features | authoritative call graph, explicit demo/reference classification |
| #45 | MTO demand orchestration | Missing canonical service | #38, #14 ledger boundary | #33, #15 | SO coverage -> PO derivation + service-date readback |
| #33 | Recursive BOM | Missing in canonical .NET; legacy reference exists | #45, #14 | #15, #36 | full requirement tree, BOM/version, coverage, shortfall |
| #14 | Time-phased material ledger | Partial/inconsistent | #38; integrates #45/#33 | #11, #15, solver, #18 | requirements, reservations, receipts/consumption, projected availability |
| #11 | Billet contingency | Partial; overlaps current material logic | #14, #33 | rolling/scenario | exact billet/heat supply allocation, ETA, RHF/hot-charge basis |
| #15 | Campaign optimizer | Partial; sort-and-fill remains authoritative | #45, #33, #14, #42 | #34, finite plan | candidates, allocations, heat structure, score/rejection evidence |
| #34 | Route-driven topology | Partial; route model richer than current projector | #15, #42, #39 | #9, #35, #16 | effective route/operations/options/queue/flow snapshots |
| #9 | Thermal | Modeled/partial | #34, #42, #39 | #16, rolling/RHF | effective thermal requirements, pair feasibility, hot/cold decision |
| #35 | Scheduling modes | Missing canonical master/solver switch | #34, #39 | #16, #43 | mode/capacity assumptions + occupancy readback |
| #16 | Late-binding resources | Partial/inconsistent | #34, #9, #35, #42, #14 | #18, replan | eligible/planned/committed/actual + revision history |
| #17 | Scenarios/outages | Partial | #14, #34, #9, #35, #16 | #43 | scenario/effective resource state + Plan Version comparison |
| #18 | Execution/genealogy | Partial | #14, #16, #34 | replan, #36 | operation actuals + recursive material genealogy |
| #19 | Diagnostics | Partial | all planning feature evidence | #43, #36 | stable diagnostic codes, objective breakdown, advisory relaxations |
| #43 | CTP/scenario/capacity | Legacy/reference + partial .NET | #17 + canonical planning phases | #36 | CTP/scenario/capacity typed results tied to canonical planner |
| #36 | Backend visibility | Partial | all functional issues | future UI | complete read/command contract inventory |
| #39 | Master wiring gate | Partial | feature masters | all planners/readers | maintained master completeness matrix |
| #40 | Logging gate | Partial | #38 call graph | operations/support | structured correlated runtime logs |
| #41 | Validation gate | Partial/dormant framework | active feature contracts | all writes | central validator/error contract evidence |
| #42 | Rule consistency gate | Partial | grade/section/resource masters | #15/#34/#9/#16/#19 | effective rule/capability readback |
| #32 | Fidelity tracker | Partial | #14/#16/#18 | #44 | resource/material history round-trip |
| #44 | End-to-end gate | Incomplete by definition | all above | parent epics/UI readiness | scenarios A-R through canonical layers |

---

# 10. Backend controls and levers completion rule

The backend visibility contract is the authoritative full catalog, but implementation reviews must check that all planning-affecting controls are either **enforced** or explicitly non-planning.

Major control families include:

- SO/MTS priority and service-date policies;
- customer/segregation requirements;
- grade chemistry/process/VD/thermal requirements;
- route operation required/optional/forbidden semantics;
- resource capability and preferred/forbidden resource rules;
- resource state/calendar/derating;
- Campaign min/target/max and mixing rules;
- furnace/heat capacity envelopes and yields;
- grade/section/product transition rules;
- CCM sequence/tundish/strand constraints;
- RHF/hot-charge constraints;
- resource scheduling mode/capacity;
- time fences/stability penalties;
- operation assignment/commitment policy;
- scenario overrides;
- solver objective priorities/time limits where exposed as controlled policy.

A configurable property that the planner ignores is worse than having no property. Wire it, clearly mark it informational, or remove it from canonical planning controls.

---

# 11. UI readiness gate

UI implementation resumes only when the backend has sufficient authoritative visibility to support it without inventing business logic.

At minimum:

- #45 demand/SO->PO facts queryable;
- #33/#14 material/BOM facts queryable;
- #15 Campaign decisions queryable;
- #34/#9/#35/#16 physical operation/resource facts queryable;
- #18 execution/genealogy queryable;
- #19 diagnostics queryable;
- #36 backend visibility contract substantially complete;
- #44 end-to-end scenarios demonstrated.

The UI may be designed earlier, but dependent screens must not ship with client-side substitutes for missing backend truth.

---

# 12. Verification rule

**Do not use GitHub Actions or CI for APS project verification.**

During implementation:

- write focused unit/integration/acceptance tests in the repository;
- perform source-level review and document expected verification;
- run build/test/runtime verification later in the intended developer environment;
- do not claim a branch is green without that real verification.

---

# 13. Final completion definition

The backend is ready for the full production UI only when:

1. #44 end-to-end scenarios work through the canonical .NET path;
2. no production path silently falls back to demo/default masters;
3. recursive material planning and finite scheduling share one material truth;
4. customer/service obligations survive manufacturing aggregation;
5. resource alternatives and physical parallelism survive through execution;
6. actual production closes material genealogy and replan without double counting;
7. diagnostics explain failures and major decisions;
8. all meaningful facts/levers have typed backend visibility;
9. cross-cutting logging/validation/master/rule consistency gates are satisfied;
10. repository authority is clear and stale documentation is archived/reference-labeled.
