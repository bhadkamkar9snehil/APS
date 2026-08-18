# APS Backend Work Program

**Status:** Canonical backend implementation program  
**Scope:** Backend/domain/planning only. Dependent production UI work waits for backend visibility and end-to-end readiness.  
**Verification rule:** **Do not use GitHub Actions or CI for APS project verification.** Verification will be performed later in the intended development environment.

Canonical companions:

- `APS_Backend_Acceptance_Audit_2026-08-18.md`
- `APS_End_to_End_Manufacturing_Planning_Flow.md`
- `APS_Demand_to_Production_Order_and_Due_Date_Model.md`
- `APS_Backend_Audit_Remediation_Map.md`
- `APS_Backend_Visibility_Contract.md`
- GitHub #2, #37, #44, #47 and #49

---

## 1. Product boundary

APS is a **manufacturing planning and scheduling system**.

It consumes authoritative facts about demand, qualified inventory, known incoming material, committed/released internal WIP, planned internal production, resource state and execution actuals.

For uncovered material:

```text
qualified supply exists by need time?
  yes -> reserve/use it
  no  -> internally manufacturable in configured plant?
            yes -> create upstream internal production requirement
            no  -> Shortfall / NotManufacturableHere
```

APS does **not** recommend procurement or transfer actions. Purchased/transferred material already present in authoritative inventory/incoming integration is simply a supply fact.

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
 -> Plan Version
 -> Work Orders + process operations
 -> execution actuals + physical material transformation
 -> inventory/WIP/remaining-demand refresh
 -> bounded local repair or broader replan
```

**Demand/material requirement is causality. Campaign is aggregation/optimization. Resource assignment is a plan/dispatch decision. WO is execution. Actual material closes the loop.**

---

## 3. Implementation-status legend

| Status | Meaning |
|---|---|
| **Implemented and authoritative** | Production uses it consistently through every applicable layer. |
| **Implemented but partial/inconsistent** | Working code exists but applicable paths/layers disagree or omit it. |
| **Modeled but not fully wired** | Domain/contracts exist but persistence/provider/planner/solver/read path is incomplete. |
| **Legacy/reference only** | Useful prototype/migration behavior exists but is not production authority. |
| **Missing** | Canonical .NET capability does not yet exist. |
| **Superseded** | Retained for history/reference only. |

A class existing is never sufficient to mark a feature authoritative.

---

## 4. Engineering issue standard

Before implementation, every primary backend issue documents:

1. current implementation state;
2. audit finding/gap;
3. target domain behavior;
4. canonical owner — evolve existing abstractions, avoid sidecars;
5. inputs/masters/controls;
6. outputs/persisted facts;
7. solver/material/execution interaction where applicable;
8. visibility/read-model requirements;
9. dependencies/blockers;
10. non-goals/product boundary;
11. compatibility/migration considerations;
12. domain acceptance scenarios;
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
 -> read model
 -> application/HTTP contract
```

If a layer is genuinely not applicable, the issue states why.

---

## 5. One-primary-issue-at-a-time discipline

For each active primary issue:

1. re-audit the current code path;
2. confirm target semantics and direct dependencies;
3. fix the canonical root abstraction;
4. complete all applicable layers;
5. satisfy applicable #39/#40/#41/#42/#36 cross-cutting requirements while touching that path;
6. document migration/fallback behavior;
7. add focused acceptance/regression tests in code without using GitHub Actions/CI;
8. record concrete implementation evidence in the issue;
9. close only after acceptance criteria are genuinely satisfied;
10. then start the next primary issue.

Do not run several major domain redesigns in parallel.

---

# 6. Ordered implementation program

## Phase 0 — repository authority

### 1. #46 Repository documentation cleanup/archive

Inventory and classify all substantive documentation/code-reference artifacts as Canonical, Current Implementation Note, Reference or Archive. Produce a cleanup manifest before moving files. Preserve useful legacy Python/workbook evidence while making its non-authoritative status obvious.

No backend behavior changes belong in this phase.

---

## Phase 1 — canonical boundaries and demand

### 2. #38 One authoritative production path

Establish one documented production call graph for demand/material/Campaign/route/scheduler/PlanVersion/execution/query behavior. Isolate demo/reference/fallback paths explicitly.

### 3. #45 SO item -> FG coverage -> MTO Production Order

Canonical quantity rule:

```text
SO open demand
- qualified FG coverage/reservation
= MTO finished-product manufacturing requirement
```

Different SO items remain separate demand/PO identities by default. Aggregation happens later through Campaign/Heat/Rolling/WO allocations. Component/raw-material shortage never silently shrinks the finished PO. Allocation-level customer service dates survive aggregation.

---

## Phase 2 — material requirements

### 4. #33 Recursive BOM/material-requirement graph

Create the canonical .NET multi-level BOM engine, preserving useful legacy behavior while strengthening lineage/time/UOM/version semantics. BOM depth is arbitrary master data and independent from finite-scheduling depth.

Example configured chain:

```text
FG -> billet/bloom -> liquid steel -> hot metal/DRI/scrap/alloys
   -> burden -> ore/pellet/sinter/coke/coal/other leaf material
```

### 5. #14 One time-phased material ledger/reservation engine

This becomes the sole material-coverage authority for #45, #33, #15, finite scheduling, execution and replan.

```text
ProjectedAvailable(t)
 = usable opening stock
 + known incoming receipts
 + committed internal receipts
 + APS-planned internal receipts
 + actual receipts
 - reservations
 - planned/released consumption
 - configured reserve
```

Material absent today may still satisfy a requirement days/weeks later.

### 6. #11 Billet/known-incoming/SMS-down contingency

Implement as a specialized use case of #14. Billet stock/known receipts may feed RHF/RM without unnecessary SMS production; if SMS cannot run and no qualified billet exists, retain shortfall rather than invent supply.

---

## Phase 3 — Campaigns, routes, finite schedule and scenarios

### 7. #15 Campaign/grade-sequence/heat candidate optimization

Replace production-authoritative sort-and-fill with candidate generation/selection considering allocation-level service, due-date spread, transition cost, Campaign/heat utilization, downstream feasibility, MTS behavior and stability.

### 8. #34 Route-driven manufacturing topology

ManufacturingRoute controls operation order/presence. Support materially different configured long-product routes without hard-coded EAF/LRF/VD chains. Existing/intermediate material may legitimately enter at downstream route points.

### 9. #9 Thermal/superheat/transfer constraints

Complete configuration-driven thermal feasibility through liquid steel and billet hot/cold routing. Separate superheat/casting constraints from billet hot-charge/RHF/rolling-entry constraints.

### 10. #35 Resource scheduling modes

Replace universal `NoOverlap` with master-driven physical scheduling semantics. Start with:

- Disjunctive;
- Cumulative.

Use one CP-SAT engine; do not build plant-specific schedulers or premature simulation frameworks.

### 11. #16 Late-binding resource assignment/commitment/redispatch

Complete generic lifecycle:

```text
Eligible Resources -> Planned Resource -> Commitment State -> Committed Resource -> Actual Resource
```

A rarely used qualified LRF is retained exactly like an alternate CCM. Same-type resources remain independent physical ResourceId timelines.

### 12. #17 Operating-state scenarios/outages

Apply outages, derating and temporary capability restrictions as an effective-plant-state overlay consumed by the same canonical planner. Scenario planning must not introduce a second scheduler or special contingency branch.

---

## Phase 4 — execution closure and explanation

### 13. #18 Full execution/material genealogy

Close actual transformation:

```text
heat operations -> cast/strand -> billet/bloom -> RHF/RM
 -> rolled intermediate -> TMT/cut -> bundle/coil/FG
```

Commercial lineage (`SO -> PO -> Campaign/WO allocation`) remains separate from physical genealogy.

### 14. #19 Planner-grade diagnostics

Normalize domain causes across validation, BOM/material, Campaign, route, resource, thermal, capacity, sequence, stability and execution. Provide advisory restoration/minimum-relaxation evidence without weakening hard metallurgy/customer/quality rules automatically.

---

## Phase 5 — decision services and full backend exposure

### 15. #43 CTP/scenario/capacity convergence

CTP, scenario planning and capacity analysis use the same canonical demand/material/route/resource semantics as normal planning. Rough-cut capacity and finite scheduled occupancy remain explicitly different products.

### 16. #36 Complete backend read/command surface

Every meaningful backend fact/decision/lever gets an intentional typed read/command contract before dependent production UI work.

The UI must never need to:

- read EF tables directly;
- deserialize opaque JSON for core planning facts;
- recalculate BOM/material balance;
- derive MTO PO quantity/service dates;
- infer resource alternatives;
- recreate diagnostics or genealogy.

---

# 7. Cross-cutting gates

These are implemented incrementally inside the active primary issue rather than as parallel redesign programs.

## #39 Master-data wiring

Maintain the full master chain where applicable:

`Domain -> EF/SQL -> provider -> planner -> PlanVersion -> read API`.

## #40 Standard logging

Use `ILogger<T>` throughout production code and Serilog only as the host provider. Add structured correlation/lifecycle logs to touched production paths. Logs are runtime evidence, not a replacement for Plan Version audit.

## #41 Validation

Use FluentValidation for application/master boundary validation; domain/application services own business invariants; solver owns finite feasibility.

## #42 Effective rule consistency

Campaign, route, solver, scenario and redispatch consume one effective capability/transition interpretation. A declared planning lever must be wired, explicitly informational, or removed.

## #32 Operational/material fidelity tracker

Not an implementation owner. Closes only after #14/#16 plus execution/replan/readback prove the required behaviors.

## #44 Final end-to-end gate

Parent epics do not close until #44 scenarios demonstrate the complete canonical .NET loop.

---

# 8. Issue acceptance matrix

| Issue | Concern | Audit status | Upstream dependencies | Downstream consumers | Required evidence before closure |
|---|---|---|---|---|---|
| #46 | Repository/document authority | Missing cleanup pass | none | all work | cleanup manifest + canonical/reference/archive index |
| #38 | Canonical production path | Partial/inconsistent | #46 | all features | authoritative call graph + explicit demo/reference classification |
| #45 | MTO demand orchestration | Missing canonical service | #38, #14 boundary | #33, #15 | SO coverage -> PO derivation + allocation-level service readback |
| #33 | Recursive BOM | Missing canonical .NET; legacy reference exists | #45, #14 | #15, #36 | complete requirement tree/BOM/version/coverage/shortfall |
| #14 | Time-phased material ledger | Partial/inconsistent | #38; integrates #45/#33 | #11/#15/solver/#18 | requirements/reservations/receipts/consumption/projected availability |
| #11 | Billet contingency | Partial/overlapping | #14/#33 | rolling/#17 | exact supply allocation/ETA/RHF-hot-charge basis |
| #15 | Campaign optimization | Partial; sort-and-fill authoritative | #45/#33/#14/#42 | #34 | candidates/allocations/heat structure/objective/rejection evidence |
| #34 | Route topology | Partial; master richer than projector | #15/#42/#39 | #9/#35/#16 | effective route/operations/options/queue/flow |
| #9 | Thermal | Modeled/partial | #34/#42/#39 | #16/RHF/RM | effective thermal requirements/pair feasibility/hot-cold decision |
| #35 | Scheduling modes | Missing canonical mode switch | #34/#39 | #16/#17/#43 | mode/capacity assumptions + correct occupancy |
| #16 | Late-binding resources | Partial/inconsistent | #34/#9/#35/#42/#14 | #17/#18/replan | eligible/planned/committed/actual + revision history |
| #17 | Scenarios/outages | Partial | #14/#34/#9/#35/#16 | #43 | scenario/effective resource state + PlanVersion comparison |
| #18 | Execution/genealogy | Partial | #14/#16/#34 | replan/#36 | operation actuals + recursive physical genealogy |
| #19 | Diagnostics | Partial | all planning feature evidence | #43/#36 | stable domain codes/objective breakdown/advisory restoration |
| #43 | CTP/scenario/capacity | Legacy/reference + partial .NET | #17 + canonical planning phases | #36 | typed decision-service results tied to canonical planner |
| #36 | Backend visibility | Partial | all functional issues | production UI | complete typed read/command inventory |
| #39 | Master wiring gate | Partial | feature masters | all planners/readers | master completeness matrix |
| #40 | Logging gate | Partial | #38 call graph | support/operations | structured correlated runtime logs |
| #41 | Validation gate | Partial | active feature contracts | all writes | central validators/stable errors |
| #42 | Rule consistency gate | Partial | grade/section/resource masters | #15/#34/#9/#16/#19 | effective rule/capability readback |
| #32 | Fidelity tracker | Partial | #14/#16/#18 | #44 | resource/material history round-trip |
| #44 | End-to-end gate | Incomplete by definition | all above | parent epics/UI readiness | scenarios A-R through canonical layers |

---

# 9. Planning controls/levers rule

All planning-affecting controls must be either **enforced** or explicitly marked non-planning. Major families include:

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
- RHF/hot-charge rules;
- resource scheduling mode/capacity;
- time fences/stability penalties;
- assignment/commitment policy;
- scenario overrides;
- controlled solver objective/time-limit policies.

A configurable property silently ignored by the planner is unacceptable.

---

# 10. UI readiness gate

Dependent production UI implementation resumes only when backend truth is queryable without client-side reconstruction. At minimum #45, #33/#14, #15, #34/#9/#35/#16/#17, #18, #19, #36 and #44 must provide the authoritative facts required by their workspaces.

UI design may continue as planning material, but screens do not ship ahead of missing backend contracts.

---

# 11. Verification rule

**Do not use GitHub Actions or CI for APS project verification.**

During implementation:

- write focused unit/integration/acceptance tests;
- perform source-level review and document expected verification;
- run build/test/runtime checks later in the intended developer environment;
- never claim the branch is green without that verification.

---

# 12. Final backend readiness definition

The backend is ready for the complete production UI only when:

1. #44 scenarios work through the canonical .NET path;
2. production never silently uses demo/default masters;
3. recursive material planning and finite scheduling share one material truth;
4. customer/service obligations survive aggregation;
5. eligible resources and physical parallelism survive through execution;
6. actual production closes genealogy/replan without double counting;
7. diagnostics explain failures and major decisions;
8. all meaningful facts/levers are typed/queryable;
9. master/logging/validation/rule-consistency gates are satisfied;
10. repository authority is clear and stale docs are archived/reference-labeled.
