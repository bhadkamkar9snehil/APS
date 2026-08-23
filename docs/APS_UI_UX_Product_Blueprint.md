# APS Production UI / UX Product Blueprint

**Status:** product/UX target plus current implementation constraints  
**Re-baselined:** 23-Aug-2026 against `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`

This document defines the production user experience. It is **not** current implementation-state authority; use [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md) and [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md) for that.

The original blueprint predates the production Blazor/Gantt build-out. Its product model remains useful, but statements implying that the UI is only a Planning Sandbox or that visual implementation has not begun are obsolete.

---

## 1. Purpose

The interface must expose APS as one coherent operational system:

```text
Demand
 -> Production Order
 -> inventory / supply allocation
 -> Campaign
 -> Heat structure
 -> configured process operations
 -> finite physical-resource schedule
 -> Plan Version readiness / approval / release
 -> Work Orders / execution actuals
 -> material genealogy
 -> replanning / next Plan Version
```

A planner should move through this chain in either direction without reconstructing relationships manually.

The UI is not a visual refresh of the workbook-era prototype and is not a page-per-backend-class generator.

---

## 2. Source-of-truth rule

Every UI capability is one of:

- **BackendReady** — authoritative application/query/command behavior exists; production UI may expose and act on it;
- **BackendPartial** — useful facts exist but a production action/read path is incomplete; UI must show only supported behavior;
- **Planned** — the blueprint reserves a workflow but UI must not imply the backend behavior exists.

Current `main` already contains a substantial DB-backed production UI and Gantt. The `/demo/planning` sandbox remains a separate direct-kernel reference path and must not become a second production lifecycle.

The UI never compensates for a missing backend contract by recomputing planning truth in Razor/JavaScript.

---

## 3. Product mental model

### Control

Answers:

- What is the authoritative Plan Version?
- Is it feasible, reviewed and release-ready?
- What is late, blocked or at risk?
- What changed?
- What requires action?

### Plan

Answers:

- What demand remains?
- What supply covers it and when?
- Why were Campaigns/heats formed?
- Where and when do physical operations run?
- What material feeds each requirement?

### Operate

Answers:

- What is released/running/completed/held?
- What actually happened and on which resource?
- What material was consumed/produced?
- What is immutable history?
- What remains replannable?

### Decide / Configure

Answers:

- What changes under another scenario?
- What can be promised?
- Why is something infeasible?
- What rule/master caused the decision?
- What is the impact of changing configuration?

Navigation follows these planner questions, not backend namespaces.

---

## 4. Primary users

- Production planner / PPC — plan, diagnose, compare, approve, release and replan.
- Operations / dispatch — equipment/sequence/commitment/execution focus.
- Material planner — time-phased material, reservations, supply and shortfalls.
- Customer-service/commercial — demand coverage, service status and CTP.
- Master-data owner — controlled authoring, effective values, validation and impact.
- Management — trustworthy plan health/service/bottleneck/delta with drilldown.

---

## 5. Navigation architecture

The conceptual architecture remains:

```text
CONTROL / HOME

PLAN
  Demand & Supply
  Campaign Studio
  Steelmaking & Casting
  Rolling & Finishing
  Finite Schedule
  Material Flow / Inventory

OPERATE
  Work Orders & Operations
  Actuals & Replan
  Traceability

DECIDE
  Scenarios / Compare
  CTP / Promise
  Diagnostics
  Capacity

CONFIGURE
  Plant / Resources / Routes / Capabilities
  Grades / Metallurgy / Thermal
  Materials / Sections / Packaging
  Rules / Calendars / Scenarios

AUDIT
  Plan Versions
  Integration / lifecycle evidence
```

Current shell implementation has been simplified since the first blueprint: several tiny navigation/context/theme wrappers were removed after their responsibilities were consolidated. The product navigation intent remains; do not recreate obsolete wrapper components simply to match the original file structure.

---

## 6. Persistent shell principles

### Plan context

The current Plan Version remains visible independently of navigation and must expose, as contracts permit:

- Plan Version / baseline / scenario;
- lifecycle status;
- horizon/reference time;
- solver/result state;
- created/released state;
- stale-data/context warning where relevant.

Global actions are enabled only from backend lifecycle truth.

Current lifecycle must reflect the implemented approval boundary:

```text
Draft -> Feasible -> Approved -> Released
```

The UI must not present a direct Feasible -> Released shortcut.

### Workspace header

State the planner question, not redundant branding.

### Main canvas

Visualization/work-surface first; dense tables support exact review/drilldown.

### Context inspector

Selected SO/PO/Campaign/Heat/Operation/Resource/Material/WO/Diagnostic/Plan Version should expose identity, current state, planning reason/basis, commercial/physical lineage, constraints, timing/quantity, diagnostics and safe actions.

---

## 7. Selection and lineage

Selection should propagate through compatible views.

```text
SO item
 -> PO
 -> Campaign allocation
 -> Heat / route operation
 -> scheduled physical resource
 -> material path
 -> WO / actual material
```

Backward physical trace must remain distinct from commercial allocation trace:

```text
bundle/coil/FG
 -> rolled output/input
 -> billet/strand/cast
 -> heat
```

and separately:

```text
WO/operation allocation
 -> PO
 -> SO item/customer demand
```

Do not collapse planned pegging, reservations and actual genealogy into one ambiguous link type.

---

## 8. Plan lifecycle UX

The current target journey is:

```text
input readiness
 -> calculate/replan
 -> inspect diagnostics/service/material/schedule
 -> compare baseline/candidate
 -> persisted readiness
 -> Approve
 -> Release
 -> execute
 -> actuals
 -> child replan
```

### Readiness/approval

Current backend readiness already evaluates persisted material/supply evidence and persisted MTO service-completion evidence. UI must render these findings directly.

A feasible plan is not automatically approved. An approved plan is not releasable if it is no longer active or readiness has become invalid under the persisted release rules.

### Release

Show exactly what lifecycle transition is occurring and what Plan Version is being released. Work Orders/operations derive from immutable persisted Plan Version structure, not client-reconstructed payloads.

---

## 9. Control / Home

The landing surface should behave as a plan-health instrument panel rather than a generic card dashboard.

Priorities:

- Plan Version/status/trust;
- release readiness;
- demand/service exposure;
- material shortfall/confidence;
- physical resource pressure;
- critical diagnostics;
- plan delta versus baseline;
- direct navigation to contributing entities.

Summary values must drill to their sources.

---

## 10. Demand & Supply

Primary question: **how will each demand requirement be satisfied?**

Expose:

- SO/item/customer;
- MTO/MTS Production Order manufacturing need;
- quantity/service date/priority;
- grade/material/final section;
- qualified FG coverage;
- intermediate/known incoming/internal planned coverage;
- uncovered/shortfall state;
- projected completion/service status;
- immutable requirement snapshot/basis.

Current production scope is manufacturing-only. Do not present speculative procurement/transfer recommendations unless that product boundary is deliberately changed in backend code/contracts/tests.

---

## 11. Campaign Studio

Explain Campaign formation through:

- PO allocation matrix;
- grade sequence;
- heat structure and furnace-feasible quantities;
- route/section/segregation constraints;
- transition/effective rule source;
- service implications;
- candidate/rejection evidence as #19/#36 expose it.

Manual intervention must become validated constraints/preferences followed by replan/reoptimization; never directly rewrite solved Campaign truth in the browser.

---

## 12. Steelmaking & Casting

Do **not** hard-code the UI to `EAF -> LRF -> VD -> CCM`.

Render the configured route and actual process identities returned by the backend. VD may be required/optional/forbidden. Additional process taxonomy is scope-driven under #62.

For each operation expose as contracts mature:

- eligible resources;
- planned/committed/actual resource;
- start/end/duration;
- queue/transfer/thermal evidence;
- execution state;
- allocation/lineage.

CCMs remain distinct physical resources. Cast/sequence/strand identity must not be visually pooled by equipment type.

---

## 13. Rolling & Finishing

Primary question: **which material feed reaches which configured downstream operation and why?**

Current #56 thermal logic means UI should expose:

- billet source;
- planned/actual thermal basis;
- transfer/wait assumption;
- hot-direct / hot-buffered / reheat-required decision;
- why direct hot charge was rejected;
- whether reheating is thermal-driven or required separately by route/order policy.

Do not imply `CCM -> RHF -> RM` is universal. The configured route may be direct hot roll, reheated, billet-only or multi-stage downstream processing.

---

## 14. Finite Schedule Board — central workbench

The Gantt is already a major implemented workbench, not a future concept.

Current requirements/behavior include:

- one row per physical resource within hierarchy;
- synchronized resource grid and UTC timeline;
- virtualized rows/time;
- calendars/unavailable spans;
- operation blocks;
- Now/reference/frozen-fence markers;
- baselines/campaign/dependencies/execution overlays;
- zoom/pan/fit/reset;
- keyboard/accessibility behavior;
- multi-selection;
- staged move/bulk-move proposals;
- authoritative move validation;
- resource-load/capacity synchronization;
- inspector/analysis surfaces;
- released-plan edit protection.

### Dragging

A drag is a **proposal**, not a direct schedule mutation. Preview/apply must be validated through canonical backend constraints and persist a child Plan Version/replan outcome where applicable.

Atomic bulk moves are evaluated as one final proposed schedule.

### Component consolidation

The absence of the old standalone `GanttBaselineLayer`, `GanttCalendarLayer`, `GanttCampaignLayer`, `GanttExecutionLayer`, `GanttProposalLayer` etc. does not mean those behaviors were removed. Current `GanttResourceLane`/viewport scene owns several aligned overlays directly after Ponytail cleanup.

See [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md).

---

## 15. Material Flow

Expose the actual canonical material facts:

- opening/qualified inventory;
- known incoming receipts;
- internal planned/committed receipts;
- reservations/ownership;
- planned consumption;
- actual adjustments;
- projected availability;
- shortfall/late/non-manufacturable reason;
- required-at time.

Never reconstruct material availability by subtracting displayed Campaign quantities in the UI.

Held/blocked/rejected material remains visible physical state but excluded from qualified usable supply.

---

## 16. Execution / replan

WO is an execution container; process operation remains scheduling truth.

Expose:

- planned/committed/actual resource;
- planned vs actual start/end/quantity;
- execution state/provenance;
- actual material input/output where implemented;
- what is completed/running/committed/frozen;
- remaining replannable work;
- baseline versus recovery/child Plan Version.

#18 remains the backend owner for completing full physical material transformation/genealogy and actual-state closure.

---

## 17. Traceability

Keep two coordinated graphs:

### Commercial

```text
SO/item -> PO -> Campaign/Heat/WO/operation allocations
```

### Physical

```text
source lot/heat -> cast/strand -> billet -> downstream transformations -> FG unit
```

Render incrementally; do not load the whole plant genealogy by default.

---

## 18. Scenario / Compare

Existing Plan Compare is a real foundation. Do not build a separate scenario-only comparison engine.

#57 should extend canonical persisted comparison to include:

- service;
- material requirements/coverage/shortfalls;
- Campaign/heat composition;
- capacity/resource changes;
- diagnostics;
- scenario-assumption attribution.

UI prioritizes delta rather than showing two independent giant plans.

---

## 19. CTP / Promise

CTP must consume the same planning kernel/material/route/resource rules as normal planning.

Output should explain:

- feasible quantity/date alternatives;
- stock/known/planned-internal basis;
- existing/new manufacturing path where applicable;
- earliest achievable date;
- resource/material assumptions;
- blocker/diagnostic when infeasible.

No unexplained green/red promise answer.

---

## 20. Diagnostics

Normalize into stable backend-coded evidence:

- severity;
- hard/soft;
- category/code;
- affected entities;
- evidence;
- consequence;
- advisory restoration guidance;
- source Plan Version/stage.

Categories include demand/service, material, Campaign, route, capability/resource, thermal/queue, transition/sequence, calendar/capacity, time fence/stability and execution.

Do not parse human messages to determine diagnostic behavior.

---

## 21. Capacity

Keep two clearly distinct products:

- **rough-cut capacity** — estimate/screening;
- **finite occupancy** — solved schedule using actual physical resource scheduling mode/capacity/calendar semantics.

The Gantt already includes a synchronized capacity/resource-load region. Historical views must use persisted Plan Version assumptions where available, not mutable current master reinterpretation.

---

## 22. Master-data workbench

A MasterData UI foundation already exists. The product target remains relationship/effective-value driven rather than unrestricted spreadsheets.

#60/#39/#41 own the missing production authoring guarantees:

- typed validated commands;
- effective rule/value preview;
- reference existence and numeric invariant validation;
- intentional retirement/deactivation semantics where history requires it;
- impact feedback where feasible.

UI must not use `DbContext` directly to bypass those application boundaries.

---

## 23. Visual language

APS should read as a **precision industrial planning instrument**, not generic SaaS.

Principles:

- dense but calm;
- strong spatial/typographic hierarchy;
- restrained depth/elevation/inset tracks;
- continuous work surfaces and split panes;
- large-enough legible operational typography;
- tabular numerals;
- status meaning separated from process/equipment color;
- no critical meaning conveyed by color alone;
- avoid repeated large rounded card grids.

Post-cleanup theme simplification is not a reason to flatten the visual system; it means fewer implementation abstractions should own the same semantic visual language.

---

## 24. Visualization policy

Use visualizations when they answer a planner question faster than rows:

- demand coverage;
- Campaign allocation;
- grade/transition sequence;
- heat/process route;
- Gantt/resource schedule;
- material time-phased availability/flow;
- capacity/load;
- plan delta;
- genealogy.

Tables remain necessary for exact values, filters, review and export.

---

## 25. Lifecycle vocabulary

Do not maintain a UI-only Plan Version state machine.

Current persisted lifecycle includes:

```text
Draft
Feasible
Approved
Released
Superseded
Failed
```

Planning-run progress (`Queued`, `Running`, etc.) is separate from persisted Plan Version lifecycle.

Desired future concepts such as warnings/acknowledgement/freeze policy must be represented only when backend contracts support them.

---

## 26. Interaction safety rules

1. Never silently loosen a hard requirement.
2. Never let a drag/inline edit overwrite schedule truth without authoritative validation.
3. Never release merely because a plan is Feasible; current release requires active Approved readiness.
4. Never hide stale Plan Version/inventory context.
5. Never merge planned pegging, reservations and actual genealogy.
6. Never present rough-cut capacity as finite occupancy.
7. Never infer interchangeability from resource type/name.
8. Never create speculative procurement/transfer actions in UI while production backend rejects them.
9. High-impact master changes require validated application commands.

---

## 27. Backend/UI contract rule

Every meaningful screen needs intentional typed reads/commands for:

- Plan Version/context/readiness;
- demand/coverage/service;
- Campaign/heat structure;
- operations/resources/calendars/capacity;
- material requirements/ledger/reservations;
- diagnostics;
- comparison/scenario/CTP;
- execution/genealogy;
- master effective values/validation.

#36 is the backend completeness gate. UI must not reconstruct missing truth from opaque JSON or unrelated DTOs.

---

## 28. Performance

Design/test at realistic planner density:

- virtualize Gantt rows/time and large tables;
- server-side page/filter large registers where appropriate;
- lazy-load detail/graph expansion;
- downsample dense charts;
- avoid sending entire Plan Versions to every page;
- keep shell responsive during planning;
- use #61 deterministic reference data to validate realistic density.

The integrated Gantt already uses row/time virtualization; future changes must not regress it.

---

## 29. Accessibility

- keyboard navigation for primary planner workflows;
- visible focus;
- no color-only status;
- tooltips supplementary only;
- high-contrast/forced-colors behavior where relevant;
- reduced motion;
- accessible business identifiers/names;
- text alternatives/summaries for complex visuals where practical.

#31 owns systematic browser/accessibility/visual regression acceptance.

---

## 30. Demo sandbox

`/demo/planning` remains useful as a calculation/component/reference harness. It is not the production workspace and must stay explicitly segregated.

Current production pages already exist; do not route production features back through the sandbox to fill a missing contract.

---

## 31. Historical prototype

The retired workbook-era HTML/UI remains historical reference at tag `v0.2.5` for feature comparison only. Do not carry forward its flat top-tab architecture, generic card repetition or presentation-side calculations as production authority.

---

## 32. Product coverage rule

For every backend capability answer:

| Question | Required answer |
|---|---|
| Where is it visible? | workspace / inspector / audit surface |
| Why does it exist? | lineage / diagnostic / rule source |
| How can it be acted on? | safe explicit command or read-only by design |
| Which Plan Version/state owns it? | visible context |
| Can it be traced? | stable entity/material/commercial links |
| Can it be compared? | delta support where meaningful |
| Is it authoritative/estimated/actual? | explicit basis |

---

## 33. Current implementation priorities

The old child-issue order (#22 -> #21 -> #23...) described first construction and is no longer a useful statement of current work.

Current UI work should follow backend truth:

1. preserve/harden the integrated Gantt/workbench;
2. expose #16 eligible/planned/committed/actual resource and redispatch workflow when backend-authoritative;
3. deepen #18 execution/genealogy;
4. deepen #19 diagnostics;
5. extend #57/#43 scenario/compare/CTP/capacity decisions;
6. close #36 typed read/command gaps;
7. complete #60 validated master authoring;
8. validate realistic density with #61;
9. complete systematic #31 browser/accessibility/performance/visual/E2E acceptance.

Production UI work is tracked under Epic #20 and its children, but issue text must also be audited against current `main` before implementation because several original child descriptions predate the integrated UI.
