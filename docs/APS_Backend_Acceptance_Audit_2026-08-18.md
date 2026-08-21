# APS Backend Acceptance Audit — 2026-08-18

Status: **canonical audit / remediation basis**

Scope: **backend only**. UI implementation is intentionally excluded. This document records the current backend state, confirmed strengths, incomplete chains, false-positive “done” states, consistency problems, domain-flexibility gaps, observability/validation findings, and the remediation order required before the backend should be treated as production-complete.

> Process rule: do not use GitHub Actions or CI as part of this work. Verification/build/test will be performed later in the intended development environment. Repository documentation, architecture, issue tracking and implementation work must not depend on GitHub Actions.

---

## 1. Audit acceptance rule

A capability is **not complete because a class exists**.

For planning/master capabilities, completion means the full chain exists and is consistent:

```text
Domain/master model
  -> SQL persistence
  -> authoritative master-data provider
  -> planning/application contract
  -> planning/solver behavior
  -> Plan Version snapshot/audit
  -> execution/replanning behavior where relevant
  -> query/read model
  -> HTTP/application exposure
```

For execution capabilities:

```text
release contract
  -> persisted execution object
  -> idempotent execution event/update
  -> actual state/history
  -> material/genealogy effect
  -> replanning effect
  -> query/read model
  -> HTTP/application exposure
```

If any required link is missing, the feature is **partial**.

---

## 2. Executive status

| Area | Current assessment | Audit conclusion |
|---|---|---|
| SO -> PO -> Campaign -> WO commercial lineage | Strong | Preserve |
| MTO + MTS | Strong foundation | Preserve and expose consistently |
| Plan Version / replan architecture | Strong foundation | Continue hardening |
| CP-SAT finite scheduling | Strong | Keep one authoritative solver path |
| Per-physical-resource sequencing | Stronger after AddCircuit work | Preserve independent ResourceId timelines |
| Resource late binding | Partial | Generic concept exists; persistence/commitment/redispatch must be fully canonical |
| Plant/resource master | Good foundation | Needs scheduling-mode semantics and route-generalization |
| Grade/metallurgy | Good foundation | Must finish thermal/transition/master wiring |
| Customer/SAP requirements | Good foundation | Must ensure complete Plan Version/read exposure |
| Campaign formation | **Incomplete** | Still materially ordered sort-and-fill within compatibility groups |
| Heat formation | Improved | Furnace-capacity aware but must remain coupled to route/source/BOM truth |
| Steelmaking route | **Incomplete** | Canonical projector still contains EAF/LRF/VD topology assumptions |
| CCM/cast/strand model | Good foundation | Must remove any residual premature caster binding and deepen cut/tundish physics |
| RHF/RM/downstream route | Good foundation | Resource scheduling mode currently too universally disjunctive |
| Time-phased material | Partial | Future/committed supply work exists, but full BOM-level material planning is not canonical .NET yet |
| Full multi-level BOM | **Major .NET gap** | Legacy Python supports it; canonical .NET does not yet |
| MAKE/BUY/TRANSFER sourcing | Partial/improving | Must work recursively at every BOM level, not only billet residual |
| Actual physical genealogy | Partial | Data model strong; complete transformation chain is not yet end-to-end execution truth |
| Diagnostics | **Incomplete** | Useful issues exist, but structured post-solver diagnosis/minimum-relaxation is not complete |
| CTP / capacity / scenarios | Mixed | Legacy Python is richer; .NET surfaces must converge on the same canonical kernel |
| Backend query/read surfaces | Partial | Several useful read models exist; exposure is inconsistent/incomplete |
| Structured logging | Incomplete | Serilog package existed without a complete standard host configuration |
| Validation | Inconsistent | FluentValidation referenced but not consistently used |
| Demo/legacy fallbacks | Risk | Must be explicit opt-in; never silently activate in authoritative production planning |

---

## 3. What the canonical .NET backend currently does when BOM is absent

This point is important because the system **does currently convert demand into WOs even though full recursive BOM is not yet implemented in the canonical .NET path**.

The current .NET chain is approximately:

```text
Sales Order / stock requirement
  -> Production Order
  -> finished-goods inventory netting
  -> billet/intermediate/committed/external supply netting
  -> residual source decision
       -> MAKE quantity
       -> approved BUY / TRANSFER / MANUAL quantity
       -> UNSOURCED remainder
  -> Campaign allocation
  -> heat formation for MAKE quantity
  -> steelmaking/casting/rolling/downstream production structure
  -> finite schedule
  -> solved task/resource assignments
  -> Work Orders + operation rows
```

`CampaignPlanningService` currently derives production quantities from `ProductionOrder.RemainingQuantityMt`, finished-goods coverage, intermediate/billet supply, committed internal supply, firm external supply and source decisions. The residual `MAKE` quantity becomes `FreshSteelQuantityMt` and drives heat formation.

`PlanReleaseBuilder` then converts the **solved** campaign/heat/rolling/downstream structure into Steelmaking, Casting, Hot Rolling and configured-route Work Orders with explicit PO allocations.

Therefore current WO quantity is derived from:

- SO/PO quantity;
- inventory/supply netting;
- explicit campaign allocation;
- fresh-steel quantity;
- route/stage yield assumptions and production-structure quantities;
- solved scheduled operations.

### Why this is insufficient

This is a **steel-route quantity transformation**, not a complete material-requirements calculation.

Without a canonical recursive BOM, the new .NET engine cannot yet answer generally:

> “This 100 MT coil requirement implies X MT billet, Y MT liquid steel, Z MT hot metal, A MT sinter/pellet/ore, B MT coke/coal, C MT flux/alloy, etc., after stock and future supply at each node.”

The historical Python engine snapshot at tag `v0.2.5` could recursively explode configured BOM levels with inventory netting, yield/scrap, byproducts and cycle diagnostics. The .NET backend must preserve and strengthen that capability rather than infer all upstream material requirements from a steel-route-specific quantity model.

---

## 4. Full BOM requirement — canonical target

The canonical .NET planning material graph must be able to traverse any configured depth, for example:

```text
Customer coil / bar / section
  -> finished-product intermediate
  -> rolled feed / billet / bloom
  -> liquid steel
  -> steelmaking charge
  -> hot metal / DRI / HBI / scrap / alloys / flux
  -> BF burden / sinter / pellets / coke
  -> iron ore / coal / limestone / leaf raw material
```

The exact chain is master data.

### Critical scope distinction

**Material-planning depth is not the same as finite-scheduling depth.**

APS may calculate requirements to iron ore while finite-scheduling only:

```text
EAF/BOF -> LRF/LF -> VD/RH -> CCM -> RHF -> RM -> finishing
```

A BOM node becomes a finite operation only when the configured route/resource model says that process is APS-scheduled at this installation.

This prevents over-engineering while preserving complete material planning.

### Required recursive semantics

For each node:

1. determine the qualified requirement quantity and required-at time;
2. net on-hand qualified supply;
3. net confirmed incoming supply;
4. net released/committed internal production;
5. net APS-planned production/supply;
6. if uncovered, select effective BOM variant;
7. apply quantity-per-output, yield, scrap/loss and UOM conversion;
8. recursively explode only the uncovered remainder;
9. retain parent/path lineage;
10. handle co-products/byproducts deterministically;
11. create MAKE / BUY / TRANSFER / MANUAL / UNSOURCED supply requirements;
12. detect cycles/effective-version ambiguity and diagnose explicitly;
13. snapshot the complete tree with the Plan Version.

### No double netting

Campaign billet netting and recursive BOM netting must converge on **one** material engine. It is unacceptable for campaign planning to consume billet once and BOM planning to consume it a second time.

---

## 5. Demand, Production Orders and commercial lineage

### Confirmed strengths

- MTO Sales Order lineage is retained through Production Order allocations.
- MTS uses internal Production Orders rather than fake Sales Orders.
- Work Orders retain explicit ProductionOrder allocations.
- Campaigns may aggregate multiple POs while preserving quantities.

### Required hardening

- Plan Version snapshots must retain the exact demand/customer requirement state used by planning.
- Every supply, campaign, heat, operation and WO must remain drillable back to PO and SO/item where applicable.
- MTS min/target/max behavior must remain visible and auditable in campaign decisions.
- Customer-specific hard requirements must never be weakened by campaign aggregation.

---

## 6. Campaign formation audit

### Finding

The current `CampaignPlanningService` performs significant domain-aware work before campaign creation:

- MTO priority;
- FG netting;
- intermediate/committed/external supply allocation;
- source selection;
- compatibility grouping;
- heat formation.

However, inside a compatibility group, campaign construction remains materially dependent on ordered **sort-and-fill** up to `MaximumCampaignQuantityMt`.

### Why this matters

Campaign quality should not depend materially on PO input order when multiple feasible groupings exist.

### Required target

```text
Compatible PO pool
  -> generate candidate campaigns
  -> generate/associate feasible heat structures
  -> grade/sequence alternatives
  -> downstream feasibility signal
  -> optimize candidate set
```

Objective hierarchy should include:

1. MTO service / critical tardiness;
2. hard compatibility;
3. stability;
4. heat/campaign utilization;
5. grade/section/product transitions;
6. caster/RM feasibility;
7. MTS target deviation / overproduction;
8. setup/campaign count.

No greedy fallback may be silently labeled authoritative after optimizer infeasibility.

Issue: #15.

---

## 7. Heat formation audit

### Strengths

- Heat formation has moved away from a purely global arbitrary heat size.
- Physical furnace capacity envelopes and grade/process constraints are part of the intended design.
- Heat-to-PO allocation exists as a required truth.

### Required consistency

- Every MAKE heat must prove at least one complete feasible production route.
- Furnace capacity, ladle/transfer constraints and yield must be coupled.
- Multiple eligible EAF/primary furnaces must remain alternatives until commitment where appropriate.
- Heat formation must consume **only** internally sourced MAKE quantity.
- BUY/TRANSFER/existing billet must never create unnecessary SMS heats.
- Recursive BOM material requirements upstream of steelmaking must not be confused with finite heat formation.

---

## 8. Steelmaking route flexibility audit

### Finding

The domain model is increasingly route-driven, but the canonical steelmaking projection still contains assumptions around:

```text
EAF -> LRF -> optional VD -> CCM
```

That topology is common, but not universal.

### Required long-product flexibility

The same engine must support configured routes such as:

```text
EAF -> LF/LRF -> CCM
EAF -> LF/LRF -> VD -> CCM
EAF -> LF/LRF -> RH -> CCM
BOF -> LF/LRF -> CCM
BOF -> LF/LRF -> RH/VD -> CCM
Induction Furnace -> LF/LRF -> CCM
configured secondary metallurgy -> CCM
existing/purchased billet -> RHF -> RM
CCM -> direct/hot rolling
```

### Design rule

Do not solve this by adding more `if EAF`, `if VD`, `if BOF` branches.

`ManufacturingRouteOperation` ordering must be authoritative. Process enums are used only when an operation has distinct domain semantics.

Issue: #34.

---

## 9. Alternate-resource / operational-flexibility audit

### Canonical invariant

For **every constrained operation**:

```text
Eligible Resources
  -> Planned Resource
  -> Firm/Preferred state
  -> Committed Resource
  -> Actual Resource
```

Applies to:

- EAF / primary steelmaking;
- LRF/LF;
- VD/RH/secondary metallurgy;
- CCM;
- RHF;
- RM;
- TMT/cooling/cutting/bundling/coiling when modeled as constrained resources.

### Important conclusion

Resource usage frequency is irrelevant.

If LRF-2 is used once per year but is genuinely eligible for one heat, that alternative must survive optimization and remain available until commitment.

### Required behavior

- all eligible resources survive solve and are persisted;
- selection rationale/penalty is queryable;
- excluded-resource reasons are queryable;
- commitment is operation-specific, not only a global freeze clock;
- local redispatch uses the **same planning constraints** as initial solve;
- off-plan actuals are recorded as physical truth, diagnosed, and trigger downstream repair;
- heat/campaign identity does not change merely because the physical resource changes;
- parallel resources remain independent ResourceId timelines.

Issue: #16 and follow-up #32.

---

## 10. Resource scheduling-mode audit

### Finding

The finite scheduler currently treats physical resources predominantly as disjunctive `NoOverlap` machines.

This is valid for many tasks at the chosen grain:

- EAF;
- LRF;
- VD;
- CCM cast/sequence block;
- rolling line.

It is **not universally correct**.

### Required canonical scheduling modes

Start with the minimum useful set:

- `Disjunctive`: one task/block at once -> `NoOverlap`;
- `Cumulative`: overlapping tasks allowed up to configured capacity -> `AddCumulative`;
- extend to explicit flow/throughput only if cumulative blocks cannot model the actual plant.

Examples where cumulative behavior may be required:

- shared reheating furnace with several billets/charges resident simultaneously;
- cooling bed;
- constrained buffer/residence system.

Do not create a second simulation engine merely to model these.

Issue: #35.

---

## 11. Transition/capability-rule consistency audit

### Strengths

The code has moved toward hierarchical grade/section rules and centralized effective-rule materialization.

### Findings

- declared rule dimensions must not exist if they are ignored by the planner;
- `ProductFamily`/outlet family is potentially a real long-product sequence/changeover dimension and must either be wired end-to-end or removed;
- structure planning and CP-SAT must consume the **same effective transition resolution**;
- exact > class > family > default precedence must be identical everywhere;
- resource-specific overrides must not be interpreted differently by caster, rolling and final scheduler code.

A master-data lever that the engine ignores is more dangerous than not having the field.

---

## 12. Thermal / superheat audit

### Existing direction

The domain has useful concepts around:

- liquidus;
- superheat/casting windows;
- process temperature envelopes;
- transfer time;
- heat loss;
- hot/cold charge;
- RHF requirement.

### Finding

The end-to-end thermal model is not yet complete enough to classify this capability as finished.

### Required target

```text
upstream exit temperature envelope
 + transfer/holding heat-loss model
 + downstream entry minimum/maximum
 + heating/correction capability
 -> pair-specific feasible transfer window
```

For billets:

```text
cast discharge thermal state
 + elapsed time / buffer state
 -> hot/direct charge feasible?
 -> otherwise RHF required
```

Hard temperature violations must not degrade into generic transition penalties.

Thermal assumptions must be Plan-Version auditable.

Issue: #9.

---

## 13. CCM / cast sequence / strands audit

### Strong foundation

- multiple physical CCMs;
- independent timelines;
- logical cast-sequence concepts;
- strand count;
- heat-wise/strand-wise output direction;
- section/grade transition concepts.

### Required hardening

- eliminate any residual pre-solver caster commitment that contradicts late binding;
- logical cast-sequence formation and physical caster assignment must be distinct;
- same continuous sequence must stay on the same selected physical CCM;
- two separate sequences may run simultaneously on different CCMs;
- tundish life/sequence-break semantics need deeper fidelity where plant data supports it;
- billet piece/cut-pattern projection should be possible without changing lineage.

---

## 14. Billet/RHF/RM/downstream audit

### Strengths

The model supports the correct supply idea:

```text
internal cast
existing billet
external/in-transit billet
planned buy/transfer
 -> hot path or RHF
 -> RM
 -> downstream route
```

and planned packaging concepts for TMT bundles/coils exist.

### Findings

- shared RHF must not be duplicated per RM;
- RHF scheduling semantics may require cumulative occupancy rather than one-job `NoOverlap`;
- hot-charge eligibility must be thermal + route + customer/grade qualified;
- downstream resource assignment must come from solved tasks, not stale pre-solver ResourceId fields;
- route-operation plans and allocations must be Plan-Version facts, not inferred later;
- bundle/coil planning and actual lot creation must remain separate grains.

---

## 15. Time-phased material planning audit

### Canonical invariant

Inventory is **one supply source**, not the boundary of planning.

For each requirement at time `t`:

```text
ProjectedAvailable(t)
 = opening qualified inventory
 + confirmed incoming by t
 + committed internal production by t
 + APS-planned internal production by t
 + approved planned buy/transfer/manual supply by t
 + actual receipts by t
 - reservations/consumption by t
 - reserves/safety quantity
```

### Required statuses

- AvailableNow
- PlannedAvailable
- SupplyActionRequired
- Shortfall
- LateSupply
- Unsourced

### Required behavior

- downstream demand stays in the plan even when stock is absent today;
- a future MAKE receipt may satisfy a requirement days/weeks later;
- month-long campaigns consume progressive supply, not campaign-start stock only;
- confirmed and speculative/planned receipts are distinct commitment states;
- committed in-process production must re-enter replanning as protected future supply;
- actual partial production + remaining committed receipt must not be double counted;
- reservations prevent double use;
- material pools preserve customer/PO qualification until explicit sharing is proven safe.

Issue: #14 and #32.

---

## 16. Source planning audit — MAKE / BUY / TRANSFER

### Canonical rule

After qualified supply netting, uncovered material becomes an explicit source decision.

- `MAKE`: create upstream production requirement;
- `BUY`: procurement/external receipt requirement;
- `TRANSFER`: location/inter-plant supply requirement;
- `MANUAL`: explicitly approved planning assumption;
- `UNSOURCED`: no approved path.

### Required source-choice behavior

- preferred source does not outrank service feasibility;
- BUY/TRANSFER require explicit qualification/policy;
- commercial MOQ/order multiple changes purchase quantity without changing demand quantity;
- projected excess remains future inventory;
- rejected source alternatives/reasons are retained;
- internal MAKE feasibility must prove the **complete required process route**, including a rare alternate LRF/VD if that is the only valid path.

### BOM implication

This source decision must work recursively at **every BOM node**, not only billet.

For example, hot metal shortfall can create MAKE/BUY/TRANSFER according to the configured model, while iron ore can create a procurement requirement without requiring finite mine scheduling.

---

## 17. Replanning / repair audit

### Good foundation

- baseline Plan Version;
- actual execution state;
- frozen/slushy concepts;
- operation planning keys;
- local repair direction;
- committed future material concept.

### Required canonical behavior

- completed operations are immutable history;
- running operations retain actual resource/start;
- held operations remain physically constrained by actual state;
- uncommitted downstream operations may be redispatched;
- local repair reopens only the affected dependency/resource neighborhood by default;
- broader replan is explicit;
- material actuals/committed receipts and resource actuals are both fed into the same child plan;
- stability penalty protects unaffected future work.

---

## 18. Release and execution audit

### Strong direction

`PlanReleaseBuilder` builds Work Orders from solved campaign/rolling/configured-route assignments and carries PO allocations.

### Findings

- release must always use **solved** resource assignments, never assume a pre-solver plan ResourceId is final;
- WorkOrder type and ProcessOperation type remain separate concepts;
- ScheduledOperation / operation snapshot should always preserve ProcessOperationType;
- specialized heat execution logic should reuse generic operation transition/state logic where possible rather than duplicate lifecycle rules;
- commitment, actual resource and off-plan deviations must be represented consistently at operation grain.

---

## 19. Physical genealogy audit

### Model strength

The intended chain is correct:

```text
SO/PO reason lineage

Heat -> Cast -> Strand -> Billet Lot
 -> RHF/RM -> Rolled Lot
 -> TMT/Cut/Bundle or Coil
 -> FG lot
 -> inventory/SO allocation
```

External billet begins at external source/lot/certificate.

### Finding

The current domain/persistence model is ahead of end-to-end execution behavior. The complete transformation chain is not yet proven as actual material events for every downstream stage.

### Required behavior

- actual consumption lot links;
- produced material lot links;
- quantity/yield at every genealogy edge;
- recursive upstream/downstream genealogy queries;
- individual bundle/coil actual IDs;
- quality/hold/rejection state;
- WO/operation/PO/SO lineage from each physical lot.

Issue: #18.

---

## 20. Diagnostics audit

### Existing strength

The backend has useful validation/issues and domain-specific codes.

### Gap

It does not yet satisfy planner-grade post-solver explanation requirements.

### Required diagnostics

- missing/invalid master;
- no route;
- no resource candidate;
- thermal infeasibility;
- material timing shortage;
- frozen-plan conflict;
- impossible transition/sequence;
- capacity/calendar conflict;
- max queue violation;
- campaign candidate rejection;
- source rejection;
- objective/penalty breakdown.

### Required guidance

When possible, return **non-authoritative** minimum-relaxation suggestions such as:

- earliest achievable date;
- restore resource X;
- add Y MT material by time T;
- release/freeze change required;
- approved alternate route needed.

Never automatically relax metallurgy, customer hard constraints, quality or safety.

Issue: #19.

---

## 21. CTP, scenario and capacity consistency audit

### Historical reference

The retired Python prototype snapshot at tag `v0.2.5` contains historical CTP/capacity/scenario behavior for deliberate comparison only; it is not a second production engine.

### Canonical .NET rule

Normal planning, CTP, scenario planning and capacity views must use **one planning kernel**.

- CTP is a constrained what-if demand run against current committed plan/supply.
- Scenario planning modifies plant/supply assumptions and creates another Plan Version.
- Capacity reporting uses the same durations/calendars/scheduling modes as production planning.
- Rough-cut capacity and finite scheduled occupancy remain explicitly different products.

Do not maintain separate hidden heuristics that can contradict the authoritative planner.

---

## 22. Persistence/master-data wiring audit

### Finding

Several domain concepts have historically existed before they were fully wired through:

- SQL DbSet/configuration;
- master provider;
- planning request;
- Plan Version snapshot;
- query/API exposure.

### Required completeness matrix

Every configurable planning concept must have one intentional owner and a complete wiring path, including:

- plant/area/stage/resource;
- resource scheduling mode/capacity;
- resource capabilities;
- calendars/outages/derating;
- flow links;
- routes and route operations;
- route-resource capability;
- grade/family/class;
- chemistry;
- process requirements;
- customer requirement overrides;
- cross-sections/material specs;
- packaging;
- transition rules;
- thermal profiles;
- assignment/commitment policies;
- sourcing rules;
- BOM and BOM versions/components;
- scenarios;
- stock policies.

No request caller should have to manually remember critical masters that SQL already owns.

---

## 23. Backend query/API exposure audit

### Finding

Several useful query/read-model methods have existed without HTTP exposure. Therefore backend facts can currently be “implemented but invisible.”

### Acceptance rule

Every meaningful planner fact needs:

- stable ID/reference;
- read model;
- filter/drill path;
- API/application contract;
- explicit designation if intentionally internal-only.

The exhaustive visibility contract is documented separately in `APS_Backend_Visibility_Contract.md` and tracked by #36.

---

## 24. Logging / observability audit

### Finding

`Serilog.AspNetCore` was referenced while host usage/configuration was incomplete. Do not add another APS-specific logging framework.

### Canonical logging architecture

```text
application/domain/planning code
  -> Microsoft.Extensions.Logging / ILogger<T>
  -> Serilog host provider
```

Use structured properties rather than string concatenation.

Important correlation dimensions:

- TraceId / RequestId
- PlanningRunId
- PlanVersionId
- ScenarioId
- ProductionOrderId
- CampaignId
- HeatId
- PlanningKey / OperationId
- ResourceId
- WorkOrderId
- MaterialRequirementId / MaterialLotId
- ExternalEventId

### Outputs

- console;
- rolling file for Windows/IIS operational support;
- optional structured sink later without changing application logging calls.

### Important distinction

Logs are **not** Plan Version audit history.

- logs answer: what happened in software/runtime?
- Plan Version facts answer: why did planning make this decision?

Both are required.

---

## 25. Validation audit

### Finding

FluentValidation is referenced but not consistently used.

### Rule

Either use it as the standard request/application validation layer or remove it. Do not carry dormant framework dependencies.

Recommended validators:

- planning/replan request;
- resource override/redispatch;
- execution update;
- resource master;
- grade master;
- route master;
- BOM master;
- sourcing rules;
- scenario override;
- release command.

### Boundary rule

- FluentValidation: request/master shape and cross-field validation.
- Domain/service: true domain invariants and state transitions.
- Solver: feasibility constraints.

Avoid three different implementations of the same rule.

---

## 26. Duplicate/dead abstraction audit

### Findings observed during audit

- parallel execution/workspace abstractions have existed for similar read responsibilities;
- some domain/audit types existed without being registered/persisted/read;
- some properties were declared before being consumed by solver logic;
- legacy/simple planning paths coexist with production paths;
- sample/demo fallbacks can mask missing production master data if allowed silently.

### Required cleanup principle

One authoritative implementation per concern:

```text
one planning orchestrator
one campaign service
one production-structure path per canonical route model
one finite scheduler
one master-data provider contract
one workspace/query facade
one operation execution-state model
one transition/capability resolver
one material ledger/BOM engine
```

Compatibility adapters may exist at boundaries, but must not become parallel planning logic.

---

## 27. Demo / legacy fallback audit

Fallback data and simple paths are useful for tests/reference, but dangerous in authoritative production planning.

### Rule

Production planning should fail explicitly if required masters are absent.

Demo fallback must be an explicit mode/opt-in and its use must be visible in the Plan Version and diagnostics.

Examples:

- default heat size;
- default machine lists;
- guessed route;
- guessed cycle times;
- simplified structure builder.

Never silently “make the plan work” by fabricating production masters.

---

## 28. Backend visibility / UI groundwork principle

UI implementation is not part of this audit, but backend acceptance includes visibility groundwork.

> There is no value in a planning fact that cannot be inspected, explained or safely acted upon.

Every core domain decision should eventually have:

1. a read contract;
2. an explanation/evidence contract where applicable;
3. a command/master contract for every supported lever;
4. stable cross-entity identifiers.

The detailed catalog is in `APS_Backend_Visibility_Contract.md`.

---

## 29. Issue state corrections from this audit

The following should remain open until their strengthened acceptance criteria are complete:

- #9 thermal/superheat;
- #14 time-phased material;
- #15 campaign candidate optimization;
- #16 late-binding resource assignment/redispatch;
- #18 physical execution genealogy;
- #19 diagnostics;
- #32 operational-flexibility/material follow-up;
- #33 full multi-level BOM;
- #34 route topology generalization;
- #35 resource scheduling modes;
- #36 backend visibility/queryability.

This audit also requires dedicated cross-cutting issues for:

- canonical pipeline/dead-abstraction cleanup;
- observability/logging;
- validation;
- master-data wiring completeness;
- transition/effective-rule completeness.

---

## 30. Recommended backend remediation order

### Phase A — canonicalization / correctness

1. single authoritative planner/read/execution abstractions;
2. master-data wiring completeness;
3. explicit production-vs-demo mode;
4. standard validation + structured logging.

### Phase B — material truth

5. recursive BOM engine in .NET;
6. one time-phased ledger at every BOM node;
7. MAKE/BUY/TRANSFER recursively;
8. no double netting;
9. committed future production and partial actual supply.

### Phase C — production-route flexibility

10. fully route-driven steelmaking operations;
11. generic alternate-resource retention/commitment/redispatch;
12. resource scheduling mode (disjunctive/cumulative);
13. thermal propagation / hot-charge/RHF decisions;
14. residual premature caster/resource-binding removal.

### Phase D — planning quality

15. candidate campaign/heat optimization;
16. complete objective decomposition;
17. domain diagnostics / minimum-relaxation guidance.

### Phase E — execution truth

18. full downstream lot transformation and genealogy;
19. operation-level actuals everywhere;
20. release/replan consistency.

### Phase F — visibility contract

21. first-class Plan Version snapshots for every important fact;
22. complete query/read facade;
23. complete HTTP/application exposure;
24. backend controls/levers catalog.

---

## 31. Backend completion definition

The backend should not be called “complete” until all of the following are true:

- a demand can be planned even when material is not currently available;
- BOM can explode to every configured leaf material;
- inventory/supply is time-phased at every BOM node;
- source actions are explicit and auditable;
- plant route can vary by configured long-product topology;
- resources remain independent and alternate-capable until commitment;
- cumulative/disjunctive resources are modeled correctly;
- hard thermal/metallurgical/customer constraints are enforced;
- campaign selection is not input-order dependent;
- actual genealogy is end-to-end;
- infeasible results explain why;
- all planning inputs/outputs are Plan-Version auditable;
- every important fact/lever is queryable for UI;
- logging/validation use standard platform libraries consistently;
- production planning has no silent demo fallback.

Until then, the .NET architecture should be described as a **strong steel APS foundation under active completion**, not a finished production backend.
