# APS Backend Work Program

**Status:** canonical backend implementation program  
**Current code authority:** `main`  
**Re-baselined:** 23-Aug-2026 against `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`  
**Current primary issue:** **#16 — late-bound resource assignment, commitment and operational redispatch**

Implementation-state detail is recorded in [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

---

## 1. Product boundary

APS is a manufacturing planning and scheduling system.

It consumes authoritative demand, qualified inventory, known incoming material, committed/released internal WIP, APS-planned internal production, resource state and execution actuals.

For uncovered material:

```text
qualified supply exists by need time?
  yes -> reserve/use it
  no  -> internally manufacturable in configured plant?
            yes -> create upstream internal production requirement
            no  -> Shortfall / NotManufacturableHere
```

Current production code deliberately rejects speculative BUY/TRANSFER/manual-supply planning controls. Purchased/transferred material that is already present in authoritative inventory/incoming integration is normal known supply. If this product boundary changes, code, tests, issues and documentation must change together.

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
 -> readiness review / Approved state
 -> persisted release
 -> Work Orders + process operations
 -> execution actuals + physical material transformation
 -> inventory/WIP/remaining-demand refresh
 -> bounded local repair or broader replan
```

Demand/material requirement is causality. Campaign is aggregation/optimization. Resource assignment is a plan/dispatch decision. Work Order is execution. Actual material closes the loop.

The route is configuration, not a hard-coded plant diagram.

---

## 3. Operational flexibility principles

### Process requirement versus resource assignment

```text
operation requirement
 -> eligible physical resources
 -> planned resource
 -> commitment state
 -> committed resource
 -> actual resource
```

Operation identity comes from the route/grade/order requirement. A planned resource must not become the identity of the operation.

### Parallel physical resources

Each physical `ResourceId` owns its own schedule/capacity semantics. Same-type resources are not pooled into one artificial sequencing circuit and may run concurrently where the physical model permits.

### Conditional process steps

VD, reheating and other operations exist because configured route/grade/order rules require or permit them, not because APS assumes a fixed `EAF -> LRF -> VD -> CCM -> RHF -> RM` chain.

### Billet thermal state — implemented

#56 is complete. The canonical planner now evaluates billet hot-charge/reheat eligibility from persisted/configured thermal evidence, transfer/wait effects and actual measured state on replan.

A delayed or buffered billet remains valid material. It may lose hot-charge eligibility and require a configured reheat path without invalidating the upstream billet-production requirement.

### Downstream outage and inventory decoupling

Where a legitimate inventory decoupling point exists, a downstream mill outage does not automatically erase otherwise-valid upstream billet production. The material may become buffered/yard inventory and later be re-evaluated using actual thermal/resource state.

---

## 4. Engineering issue standard

Before implementation, every primary backend issue should state:

1. current implementation state from `main`;
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
13. completion evidence;
14. Windows verification expectation for the completed tranche.

A feature closes only when its applicable chain is coherent:

```text
Domain/master
 -> persistence/provider
 -> application/planning contract
 -> planner/solver enforcement
 -> Plan Version evidence
 -> execution/replan where relevant
 -> typed read/API where relevant
```

A class existing is never sufficient evidence of completion.

---

## 5. One-primary-issue-at-a-time discipline

For each active primary issue:

1. audit current `main` and any newer unmerged WIP that genuinely contains the claimed work;
2. confirm target semantics and direct dependencies;
3. fix the canonical root abstraction;
4. complete all applicable layers;
5. satisfy applicable #39/#40/#41/#42/#36 concerns while touching that path;
6. add focused acceptance/regression tests;
7. verify the completed tranche on the authoritative Windows environment when claiming it green;
8. record implementation evidence in the issue;
9. integrate the completed tranche to `main`;
10. update current-state/program documentation in the same tranche;
11. then start the next primary issue.

Do not run several major domain redesigns in parallel.

---

# 6. Ordered implementation program

## Phase 0 — repository authority — COMPLETE

### #46 Repository documentation cleanup/archive — closed

Canonical/current/reference/archive authority established. The retired Python/workbook implementation remains historical at tag `v0.2.5`.

## Phase 1 — canonical boundaries and demand — COMPLETE

### #38 One authoritative production path — closed

One production lifecycle owns demand, planning, persistence, readback, approval/release and replan. Demo/reference paths are separated.

### #45 SO item -> FG coverage -> MTO Production Order — closed

Qualified FG coverage reduces manufacturing need without erasing customer-demand lineage. Repeated reconciliation is idempotent and production-order service dates survive later aggregation.

## Phase 2 — material requirements — COMPLETE FOUNDATIONS

### #33 Recursive BOM/material-requirement graph — closed

Recursive requirement causality, lineage/version/UOM semantics and internally manufacturable upstream requirements exist in the canonical .NET path.

### #14 Time-phased material ledger/reservation — closed foundation

Current stock is not the planning horizon. Inventory, known incoming, committed WIP and planned internal supply are evaluated at the time material is required.

### #11 Billet/known-incoming contingency — closed foundation

Qualified existing/future billet can feed downstream manufacturing without unnecessary SMS production; missing supply remains explicit rather than fabricated.

## Phase 3 — manufacturing aggregation, route, thermal and resource flexibility

### #15 Campaign/grade-sequence/heat candidate optimization — closed/integrated

Campaign formation uses explicit candidate/objective logic, service obligations, transitions, furnace-feasible heats, downstream feasibility, MTO/MTS policy and replan stability rather than authoritative sort-and-fill.

### #34 Route-driven pre-CCM topology — closed foundation

Configured `ManufacturingRoute` owns pre-CCM operation order/presence.

### #58 Route-driven downstream projection — closed/integrated

The downstream chain no longer pivots architecturally on the first `HotRoll`. Direct hot charge, billet-only routes, configured reheating, arbitrary downstream operations and multi-pass/inter-pass heating remain valid route configurations.

### #9 Liquid-steel thermal envelope/resource-pair constraints — closed foundation

Configured liquid-steel transfer/superheat/casting-temperature feasibility is enforced through the CCM path.

### #56 Billet thermal chain / hot charge / actual-state replan — CLOSED/INTEGRATED

Implemented behavior includes:

- time/temperature-aware billet hot eligibility;
- transfer/wait/holding-loss effects;
- order/grade narrowing;
- configured optional-reheat fallback for thermally ineligible direct paths;
- conservative unknown/yard handling;
- actual measured state overriding stale categorical/planned state during replan;
- Plan Version readback of thermal decision basis.

**Do not list #56 as current work.**

### #35 Resource scheduling modes/cumulative capacity — closed foundation

Physical resource occupancy is master-driven; universal `NoOverlap` is not assumed. Historical workbench capacity now uses persisted assumptions and the same compounded derating semantics as the solver path.

### #16 Late-bound resource assignment, commitment and operational redispatch — CURRENT PRIMARY

Existing foundations include alternative finite-schedule resource options, independent physical timelines and solver-owned CCM selection for the completed casting slice. The remaining target is the complete generic lifecycle:

```text
Eligible Resources
 -> Planned Resource
 -> Commitment State
 -> Committed Resource
 -> Actual Resource
 -> auditable bounded redispatch/local repair
```

Required completion includes:

- retain genuinely eligible alternatives after solve;
- persist eligibility/exclusion evidence where required;
- operation-specific commitment policy/state;
- generic dispatch/redispatch, not process-specific special cases;
- revalidation of material, route, thermal, transfer, queue, sequence, calendar and capacity constraints;
- child Plan Version / revision history;
- actual resource as immutable physical truth once executed;
- readback of alternatives, commitment and redispatch history.

### #17 Operating-state scenarios/outages — closed foundation

Resource outages/derating/restrictions are effective scenario overlays consumed by the canonical planner. Richer material contingency/comparison remains #57.

## Phase 4 — execution closure and explanation

### #18 Full execution/material genealogy — NEXT AFTER #16

Close actual transformation and actual-state feedback across the configured route while keeping commercial allocation lineage separate from physical material genealogy.

### #19 Planner-grade diagnostics

Normalize domain causes across validation, material, campaign, route, resource, thermal, capacity, sequence, stability and execution. Provide advisory restoration evidence without silently weakening hard rules.

## Phase 5 — scenario/decision services and exposure

### #57 Scenario material contingency + richer Plan Version comparison

Prove resource-outage/material-contingency behavior through the canonical material kernel and extend comparison beyond operation movement to service/material/campaign/capacity/diagnostic differences.

### #43 CTP/scenario/capacity convergence

CTP, scenarios and capacity analysis must consume the same canonical demand/material/route/resource semantics as normal planning. Rough-cut capacity stays distinct from finite occupancy.

### #36 Complete backend read/command surface

Every meaningful planning fact, decision and lever must have an intentional typed contract. UI must not reconstruct planning truth.

## Phase 6 — configuration/reference acceptance readiness

### #60 Validated operational master authoring

Complete intentional validated authoring for thermal, scenario and resource-scheduling masters on top of the canonical persistence/admin path.

### #61 Deterministic integrated-steel reference dataset

Persist a realistic deterministic reference dataset dense enough to exercise the canonical SQL-backed lifecycle.

### #44 Final end-to-end manufacturing-planning acceptance gate

Close only when the complete canonical loop is demonstrated across the required scenarios.

### Scope-gated #62 Process taxonomy

Add only process identities evidenced by target/reference plants; taxonomy must not become topology.

### Independent #59 Tailwind cross-platform verification

Portable pinned build logic exists; clean supported-host verification remains independent of backend sequencing.

---

# 7. Cross-cutting gates

- **#39 Master wiring:** maintain Domain -> persistence -> provider -> planner -> PlanVersion -> read chain for planning-affecting masters.
- **#40 Logging:** use structured `ILogger<T>` production observability; logs complement Plan Version evidence.
- **#41 Validation:** boundary validation and domain/solver ownership remain distinct.
- **#42 Effective rule consistency:** one effective capability/transition interpretation must be reused across consumers.
- **#32 Operational/material fidelity:** remains a tracker until #16/#18/#36 close the remaining invariants.
- **#44 End-to-end gate:** final acceptance, not issue-count optics.

---

# 8. Current acceptance matrix

| Issue | Concern | Current state |
|---|---|---|
| #46 | Repository/document authority | Closed |
| #38 | Canonical production path | Closed |
| #45 | MTO demand orchestration | Closed |
| #33 | Recursive BOM | Closed foundation |
| #14 | Time-phased material | Closed foundation |
| #11 | Billet/known incoming | Closed foundation |
| #15 | Campaign optimization | Closed/integrated |
| #34 | Pre-CCM route topology | Closed foundation |
| #58 | Downstream route topology | Closed/integrated |
| #9 | Liquid-steel thermal | Closed foundation |
| #56 | Billet thermal state | **Closed/integrated** |
| #35 | Resource scheduling modes | Closed foundation |
| #16 | Late resource binding/redispatch | **Current primary / open** |
| #17 | Resource-state scenarios | Closed foundation |
| #18 | Execution/genealogy | Open; next primary after #16 |
| #19 | Diagnostics | Open |
| #57 | Scenario material + richer compare | Open |
| #43 | CTP/scenario/capacity convergence | Open |
| #36 | Read/command exposure | Open |
| #60 | Validated master authoring | Open |
| #61 | Persisted reference dataset | Open |
| #44 | Final integrated acceptance | Open |
| #62 | Process taxonomy | Scope-gated |
| #59 | Tailwind host matrix | Independent |

---

# 9. Plan release lifecycle now integrated

Current `main` contains a persisted approval/readiness boundary that older work-program versions did not describe:

```text
Draft -> Feasible -> Approved -> Released
```

Approval/readiness uses persisted Plan Version evidence rather than live mutable planning state. Release requires an active Approved plan and the persistence repository rejects direct lifecycle bypasses.

Readiness includes material/supply evidence and persisted MTO service-completion checks. The service-date model is still expected to evolve toward explicit customer-required/production-required-by/allocation-grain semantics; do not treat the current `RequiredDate` comparison as the final model.

---

# 10. Verification rule

The old blanket instruction “do not use CI” is obsolete.

**Authoritative APS automated verification is the shared self-hosted Windows Azure DevOps `EOS` agent running repository-owned `build/verify.ps1`.** GitHub Actions or hosted CI are not substitutes.

For a commit to be called green, inspect the exact Windows run/evidence for that exact SHA. The verifier restores, performs a full Release build, runs every solution-registered test project and publishes a self-contained `win-x64` DesktopHost smoke artifact.

Latest recorded evidence for `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`: 0 build warnings/errors, 336/336 tests, Windows publish, SQLite quick-check OK and live published-desktop verification of the released 105-operation/8-resource baseline.

See [`windows-ci.md`](windows-ci.md).
