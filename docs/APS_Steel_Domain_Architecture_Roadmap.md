# APS Steel-Domain Architecture and Roadmap

**Status:** canonical steel-domain architecture and roadmap  
**Re-baselined:** 23-Aug-2026 against current `main`  
**Current primary implementation issue:** #16 — late-bound resource assignment, commitment and operational redispatch  
**Historical pre-rebaseline roadmap:** [`archive/domain-roadmaps/APS_Steel_Domain_Architecture_Roadmap_pre_2026-08-23.md`](archive/domain-roadmaps/APS_Steel_Domain_Architecture_Roadmap_pre_2026-08-23.md)

This document describes the **current** steel-domain architecture and the remaining roadmap. It supersedes the earlier roadmap that still described EAF/LRF/VD modeling as a major future gap and the production UI as only a sandbox.

Implementation-state detail is also summarized in [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md). Backend work order is governed by [`APS_Backend_Work_Program.md`](APS_Backend_Work_Program.md).

---

## 1. Architectural position

APS is a manufacturing Advanced Planning & Scheduling system for integrated steel production.

The canonical causal chain is:

```text
SAP Sales Order item / MTS requirement
 -> customer + product + grade requirement resolution
 -> qualified finished-goods coverage
 -> MTO/MTS Production Order manufacturing requirement
 -> recursive BOM/material requirement graph
 -> time-phased material coverage and reservations
 -> internally manufacturable upstream requirements / explicit shortfall
 -> Campaign candidate optimization
 -> grade sequence + furnace-feasible heats
 -> configured ManufacturingRoute operations
 -> finite material/resource/thermal schedule
 -> immutable Plan Version
 -> readiness review / Approved
 -> persisted release
 -> Work Orders + process operations
 -> execution actuals + physical material transformation
 -> inventory/WIP/remaining-demand refresh
 -> bounded repair or broader replan
```

Demand/material requirement is causality. Campaign is manufacturing aggregation/optimization. Resource assignment is a planning/dispatch decision. Work Orders are execution artifacts. Actual material and actual resource/time/quantity close the loop.

The steel domain is explicit, but **plant topology and equipment count are data-driven**. APS must not encode one fixed plant diagram.

---

## 2. Product boundary

APS plans what the configured plant can manufacture.

For every uncovered material requirement:

```text
qualified supply available by need time?
  yes -> consume/reserve it
  no  -> can configured internal manufacturing satisfy it?
          yes -> create/schedule upstream manufacturing requirement
          no  -> explicit Shortfall / NotManufacturableHere
```

Important rules:

- material absence **does not delete manufacturing demand**;
- a month-long campaign does not require all material to exist on day one;
- future internal production, released/running WIP and authoritative known incoming material may satisfy later requirements;
- APS production planning does not invent procurement, transfer or supplier decisions;
- purchased/transferred material already present in authoritative inventory/incoming integration is simply known supply.

---

## 3. Current implementation maturity

The earlier roadmap's maturity percentages are obsolete. Current `main` already contains the following canonical foundations.

| Area | Current state |
|---|---|
| SO -> FG coverage -> MTO PO | Implemented and persisted |
| MTO + MTS | Implemented |
| Recursive BOM/material causality | Implemented foundation |
| Time-phased material coverage/reservation | Implemented foundation |
| Known future billet / committed WIP | Implemented foundation |
| Campaign candidate optimization | Implemented/integrated |
| Grade sequence / transition economics | Implemented foundation |
| Furnace-feasible heat formation | Implemented |
| Configured pre-CCM route | Implemented foundation |
| EAF/LRF/VD/other configured steelmaking steps | Route-driven; not hard-coded topology |
| Multiple physical CCMs | Implemented with independent physical timelines |
| Route-driven downstream projection | Implemented/integrated |
| Direct hot charge vs reheating | Configured/thermal decision, not universal RHF |
| Billet thermal aging / actual-state replan | Implemented/integrated (#56) |
| Liquid-steel thermal/resource-pair constraints | Implemented foundation |
| Multiple physical rolling resources | Implemented |
| Unary and cumulative scheduling modes | Implemented foundation |
| Resource outage/derating scenarios | Implemented foundation |
| Immutable Plan Versions | Implemented |
| Plan compare / workbench readback | Implemented foundation |
| Approval/readiness/release | Implemented persisted lifecycle |
| Work Orders / process operations | Implemented |
| Execution services and genealogy foundations | Implemented, deeper closure remains #18 |
| Production Blazor UI / planner workbench | Implemented foundation |
| Central Gantt workbench | Major overhaul integrated; further planner-grade depth remains |
| Windows verification | Shared EOS Azure Windows verifier established |

The remaining work is no longer “build the steel domain from Heat -> CCM upward.” It is to close **operational dispatch flexibility, execution truth, explanation, scenario consistency, complete read/command exposure, master authoring and integrated acceptance** on top of the now-established steel model.

---

## 4. Plant, stage and physical-resource semantics

### Plant

A steel works / planning site.

### Area

A major operating area such as steelmaking, casting, billet/intermediate storage, rolling or finishing.

### Process stage / operation type

A manufacturing capability or route step. It is **not** a physical machine.

Examples:

- primary steelmaking;
- LRF/LF/secondary metallurgy;
- VD/RH/AOD/VOD or other configured treatment;
- continuous casting;
- reheating;
- hot rolling;
- cold rolling;
- TMT/quench;
- cooling/cutting/bundling/coiling/finishing.

### Resource

One physical independently constrained equipment instance, for example:

```text
EAF-1
EAF-2
LRF-1
LRF-2
VD-1
CCM-1
CCM-2
RHF-1
RM-1
RM-2
```

Each physical `ResourceId` owns its own:

- capability/eligibility;
- calendar;
- operating state;
- scheduling mode/capacity basis;
- throughput/duration semantics;
- sequence/transition occupancy;
- planned, committed and actual execution relationship.

Parallel resources are not collapsed into an artificial shared sequence merely because they have the same type.

---

## 5. Manufacturing route is authoritative topology

APS does **not** assume a universal:

```text
EAF -> LRF -> VD -> CCM -> RHF -> RM
```

The configured `ManufacturingRoute` determines the ordered manufacturing operations.

Valid configured examples include:

```text
primary steelmaking -> LRF -> CCM -> HotRoll
primary steelmaking -> LRF -> VD -> CCM -> Reheat -> HotRoll
CCM -> HotRoll
CCM -> Reheat -> HotRoll
CCM -> HotRoll -> ColdRoll -> Finish
CCM -> HotRoll -> Reheat -> HotRoll
billet inventory -> Reheat -> HotRoll
```

An operation may be:

- required;
- optional under grade/order/material/thermal policy;
- forbidden by the applicable requirement.

### VD and other secondary-treatment steps

VD is present because the configured route and effective grade/order requirement require or permit it. The same principle applies to RH, AOD, VOD or other treatment operations. The planner must not infer process semantics from equipment naming alone.

### Reheating

Reheating is conditional, not universal.

Direct hot charge is valid only when all applicable route/order/thermal/flow constraints permit it. Cold/yard billet, a route requiring RHF, loss of hot continuity or measured/estimated thermal ineligibility selects the configured reheat path.

If reheating is required but no eligible configured resource/path exists, APS reports named infeasibility; it does not invent a furnace or bypass the requirement.

---

## 6. Material and billet planning

The material model separates **requirement identity** from **current stock**.

A rolling requirement can remain valid even when the billet does not yet exist. APS may satisfy it from:

- current qualified billet inventory;
- authoritative future incoming billet;
- released/running internal cast output;
- APS-planned internal cast output.

If none is available by the need time, the requirement remains visible as late supply/shortfall.

### Inventory decoupling

A configured inventory/buffer point intentionally breaks guaranteed hot continuity. Material remains valid supply but later hot-charge eligibility must be re-established from the current thermal state and configured route.

This is why a downstream mill outage does not necessarily erase an upstream billet-production requirement. The plant may continue producing intermediate stock if the configured planning policy and capacity make that valid.

---

## 7. Campaign, grade sequence and heat structure

Campaign formation is not production-authoritative sort-and-fill.

Current planning evaluates candidate grouping/sequence/heat structures using explicit technical and service economics including:

- PO allocation-level service obligations;
- grade/sequence compatibility;
- customer/segregation constraints;
- caster/input format compatibility;
- route/downstream feasibility;
- transition prohibition/time/penalty;
- furnace-feasible heat envelopes;
- heat-target deviation/utilization;
- MTO/MTS policy;
- campaign/setup economics;
- early-production/service cost;
- replan stability against the persisted baseline.

Campaigns aggregate manufacturing requirements, but PO/SO quantity/date/customer identity survives through allocation records.

Replan stability is a **soft** objective: hard technical feasibility and customer-service requirements may force a different grouping.

---

## 8. Finite scheduling and resource semantics

The CP-SAT finite scheduler owns constrained resource/time decisions.

Current foundations include:

- optional resource assignment with exactly-one selection;
- independent physical-resource schedules;
- unary finite capacity where appropriate;
- cumulative capacity where configured;
- resource calendars/downtime;
- process/material dependencies;
- min/max transfer or queue constraints where modeled;
- route/resource qualification;
- transition/setup sequencing;
- time fences and plan stability;
- service obligations/tardiness;
- thermal feasibility;
- operating-state/derating semantics.

Resource type does not imply interchangeability. Eligibility comes from explicit capability/master data and the complete process/material/grade/section/flow context.

---

## 9. Thermal model

### Liquid steel

Liquid-steel planning uses configured thermal/superheat/casting constraints and transfer/resource-pair feasibility through the casting path.

### Billet thermal chain

#56 is integrated. APS now supports:

- hot/cold eligibility based on time/temperature evidence;
- transfer/wait/holding-loss effects;
- configured grade/order narrowing;
- direct hot-charge eligibility;
- optional-reheat fallback where the route permits;
- conservative handling of unknown/yard material;
- actual measured state overriding stale planned/categorical state during replan;
- persisted decision basis for historical readback.

Thermal state affects the **valid downstream route decision**; it does not erase the material or its upstream manufacturing lineage.

---

## 10. Resource assignment, commitment and operational flexibility

This is the current primary architectural gap under #16.

The target lifecycle is generic across process types:

```text
operation requirement
 -> eligible physical resources
 -> planned resource
 -> commitment state
 -> committed resource
 -> actual resource
```

A heat that is ready at LRF must be able to retain valid CCM alternatives until the commitment boundary. If CCM-1 becomes unavailable and CCM-2 remains technically feasible, the system should support a bounded, auditable redispatch without changing the heat/order identity.

The same architecture applies to other genuinely interchangeable process resources.

Completion requires:

- retain eligible alternatives after solve;
- persist eligibility/exclusion evidence where needed;
- operation-specific commitment policy;
- planned vs committed vs actual assignment;
- bounded local redispatch/repair;
- revalidation of route/material/thermal/queue/sequence/calendar/capacity rules;
- child Plan Version / revision history;
- immutable actual-resource truth after execution;
- typed readback of alternatives, commitment and redispatch history.

Do not implement this as a CCM-specific exception framework.

---

## 11. Plan Version, approval and release

Planning truth is persisted as immutable Plan Versions.

Current lifecycle includes:

```text
Draft -> Feasible -> Approved -> Released
```

with failed/superseded states where applicable.

A historical Plan Version must be explainable from persisted snapshots/assumptions rather than silently adopting live masters.

Release is identity-only:

```text
PlanVersionId
 -> persisted Plan Version snapshots
 -> readiness/approval policy
 -> persisted release service
 -> Work Orders + ScheduledOperations
```

The client must not reconstruct production structure and submit a second version of planning truth.

Readiness includes persisted material/supply evidence and MTO service-completion checks.

---

## 12. Execution and genealogy

The architecture distinguishes:

### Commercial lineage

```text
SO/item -> PO -> Campaign allocation -> Heat/Rolling/Route/WO allocation
```

### Physical genealogy

```text
Heat/cast/strand -> billet/material lot -> reheating/rolling -> downstream material -> bundle/coil/FG
```

These relationships overlap but are not the same thing.

Execution services already provide operation/work-order/heat foundations and actual material output. #18 remains the next primary after #16 to close actual transformation, actual-state feedback and route-wide genealogy rigor.

Actuals must never rewrite historical planned truth. They become new execution facts used by replan.

---

## 13. UI and central planning workbench

The statement that APS has only a Planning Sandbox is obsolete.

Current `APS.UI` contains production-oriented pages for demand/supply, Campaign Studio, steelmaking/casting, rolling/finishing, finite schedule, material flow, Plan Versions/compare, inventory and master data, plus the central planning-workbench components/state.

The Gantt is the primary planning instrument and has undergone a major overhaul. Current implementation includes synchronized resource/time workbench behavior, splitters, multi-tier scale, dependency routing, baselines/calendars/campaign/execution/proposal layers, selection/drag/preview/apply flows, capacity and analysis surfaces.

Ponytail cleanup subsequently consolidated several standalone render-layer components into fewer canonical lane/viewport paths. **That consolidation removed duplicate plumbing, not the represented planning layers.**

Remaining Gantt/product work should be read from:

- [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md);
- [`APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`](APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md);
- [`APS_UI_Implementation_Plan.md`](APS_UI_Implementation_Plan.md).

---

## 14. Current roadmap

### Phase A — late-bound dispatch and local repair

**#16 current primary**

Close the complete eligible -> planned -> committed -> actual resource lifecycle and bounded operational redispatch.

### Phase B — execution/material closure

**#18 next**

Complete route-wide execution, actual material transformation, actual-state feedback and genealogy while preserving commercial/physical lineage separation.

### Phase C — diagnostics/explanation

**#19**

Normalize material, master, campaign, route, thermal, resource, capacity, sequence, time-fence and execution causes. Explanations must identify evidence and restoration options without silently weakening hard constraints.

### Phase D — scenarios/decision consistency

**#57 and #43**

Use the same canonical material/route/resource semantics for outage/material contingency, Plan Version comparison, CTP, scenario and capacity views. Rough-cut capacity remains distinct from finite occupancy.

### Phase E — complete exposure and configuration

**#36 and #60**

Expose every meaningful planning fact/lever through typed read/command contracts and complete validated operational master authoring.

### Phase F — deterministic reference acceptance

**#61 and #44**

Persist a realistic deterministic integrated-steel reference dataset and close the full manufacturing-planning acceptance loop across demand, material, campaigns, routes, finite scheduling, Plan Versions, release, execution, replan and readback.

Scope-gated/independent work such as process-taxonomy expansion (#62) and clean-host Tailwind verification (#59) remains subordinate to the canonical domain sequence.

---

## 15. Non-negotiable steel-domain invariants

1. A physical resource is not a process stage.
2. Same-type resources are not automatically interchangeable.
3. A planned resource is not operation identity.
4. Configured route, not a hard-coded EAF/LRF/VD/CCM/RHF/RM diagram, defines topology.
5. Material shortage does not erase manufacturing demand.
6. Future material may satisfy future operations; current inventory is not the planning horizon.
7. Campaign aggregation must preserve PO/SO allocation identity and service obligations.
8. Actual physical resource/time/material never rewrites the historical plan.
9. Plan Version history is immutable and must remain explainable from persisted evidence.
10. Operational redispatch must revalidate material, thermal, sequence, queue, calendar and capacity constraints.
11. Rough-cut capacity and finite scheduled occupancy are different truths.
12. UI interactions may propose planning changes; they do not bypass the planning engine.

---

## 16. Verification

The old “never use CI” instruction is obsolete.

The authoritative APS automated verification path is the shared self-hosted Windows Azure DevOps **EOS** agent running the repository-owned [`../build/verify.ps1`](../build/verify.ps1).

A commit may be called green only when the exact Windows run/evidence for that exact SHA has been inspected. The verifier performs restore, full Release build, every solution-registered test project and self-contained `win-x64` DesktopHost publish smoke.

GitHub Actions/hosted CI are not authoritative APS verification substitutes.

See [`windows-ci.md`](windows-ci.md) and [`APS_Testing_Strategy.md`](APS_Testing_Strategy.md).
