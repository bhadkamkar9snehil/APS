# APS Production UI / UX Product Blueprint

## 1. Purpose

This document defines the production user experience for the .NET APS.

It is intentionally **not** a visual refresh of the workbook-era HTML application and it is not a collection of pages generated from backend class names. The interface must expose the planning model as one coherent operational system:

```text
Demand
  -> Production Order
  -> inventory / supply allocation
  -> Campaign
  -> Heat structure
  -> physical process operations
  -> finite resource schedule
  -> Work Order release
  -> execution actuals
  -> material genealogy
  -> replanning / next Plan Version
```

The primary UX objective is that a planner can move through this chain in either direction without losing context or reconstructing relationships manually.

---

## 2. Source-of-truth rule

UI development must always distinguish three states:

- `BackendReady`: authoritative application/domain/query behavior exists and the UI can expose it.
- `BackendPartial`: a concept exists but is not yet complete enough for a production action; UI may expose read-only/reference state only.
- `Planned`: the product blueprint reserves a UI location, but no screen may imply the behavior already exists.

At the time this document is introduced, PR #1 contains a real .NET planning backbone and a **reference Planning Sandbox**, but that page is not the production planner workspace. It runs a built-in sample scenario and displays campaign/cast/rolling/schedule/WO output directly from `IPlanningEngine`.

The production UI must therefore be built on explicit application/query contracts and DB-backed state, not by enlarging the sandbox.

---

## 3. Product mental model

The system has four user-facing layers.

### 3.1 Control

Answers:

- What is the current authoritative plan?
- Can it be trusted?
- What is late, blocked or at risk?
- What changed from the previous plan?
- What action is required now?

### 3.2 Plan

Answers:

- What demand must be satisfied?
- What supply already covers it?
- Why did APS form these campaigns and heats?
- Where and when will each physical operation run?
- Which material feeds each downstream requirement?

### 3.3 Operate

Answers:

- What has been released?
- What is running/completed/held?
- What material was actually produced or consumed?
- What is now fixed in history?
- What remaining work should be replanned?

### 3.4 Decide / Configure

Answers:

- What happens under another scenario?
- What can we promise?
- Why is a plan infeasible?
- What master/rule produced this decision?
- What would be affected if a master changes?

These layers determine the navigation. Backend module names do not.

---

## 4. Primary users

### Production planner / PPC

Primary persona. Needs complete plan creation, diagnosis, comparison, freezing, release and replanning.

### Operations / dispatch

Needs an equipment- and sequence-centric view with minimal commercial clutter.

### Material planner

Needs time-phased material availability, reservations, external/internal supply and shortages.

### Customer-service / commercial user

Needs demand coverage and CTP without understanding solver internals.

### Master-data owner

Needs controlled editing, inheritance/effective-value inspection, validation and impact analysis.

### Management

Needs trustworthy plan health, service risk, bottlenecks and deltas, with drilldown rather than separate executive-only data.

---

## 5. Navigation architecture

The production application should use a compact primary rail, not nine or ten equal top tabs.

```text
CONTROL TOWER

PLAN
  Demand & Supply
  Campaign Studio
  Steelmaking & Casting
  Rolling & Finishing
  Finite Schedule
  Material Flow

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
  Plant & Resources
  Grades & Metallurgy
  Materials & Sections
  Routes & Capabilities
  Rules & Calendars
  External Supply

AUDIT
  Plan Versions
  Integration State
```

The rail may collapse to icons, but the current Plan Version remains visible independently of navigation.

---

## 6. Persistent application shell

Every production workspace shares four persistent layers.

### 6.1 Plan context bar

Always shows:

- plan version / scenario
- baseline plan if applicable
- status
- horizon
- reference time
- solver status
- created time
- released/frozen state
- stale-data indicator

Primary actions live here only when globally valid:

- Calculate / Run Plan
- Compare
- Review / Accept
- Freeze
- Release
- Replan

The action state must come from backend lifecycle rules. UI code must not infer that an action is safe merely because a button can be clicked.

### 6.2 Workspace header

Shows the current planner question, not redundant branding.

Examples:

- `Which demand is still uncovered?`
- `How did APS construct Campaign C-1042?`
- `Where is heat H-188 scheduled?`
- `What changed after the CCM outage?`

### 6.3 Main canvas

Visualization-first area. Dense tables are supporting views or synchronized lower panes.

### 6.4 Context inspector

A persistent right-side inspector opens for selected entities.

Supported entity types include:

- Sales Order / item
- Production Order
- Campaign
- Campaign Heat
- Cast Sequence
- Process Operation
- Resource
- Material supply / reservation
- Material lot / bundle / coil
- Work Order
- Diagnostic
- Plan Version

Inspector sections:

1. Identity
2. Current state
3. Planning meaning / reason
4. Commercial lineage
5. Physical lineage
6. Constraints / requirements
7. Timing / quantities
8. Related diagnostics
9. Available safe actions

The inspector is the main mechanism for cross-screen continuity; avoid repeated modal dialogs.

---

## 7. Global selection and lineage behavior

Selecting an entity must propagate context through compatible views.

Example:

```text
Select SO-1001 / Item 10
  -> Demand row highlights
  -> its PO allocation highlights
  -> Campaign segment highlights
  -> allocated heats highlight
  -> scheduled operation path highlights
  -> material source path highlights
  -> related WOs / actual lots highlight
```

Backward selection works the same way:

```text
Select bundle BU-0098
  -> RM operation
  -> consumed billet lots
  -> cast / strand / heat
  -> campaign
  -> PO allocations
  -> SO/items
```

This is more important than preserving a particular page layout.

---

## 8. Plan lifecycle UX

The main planner journey is:

```text
Input readiness
  -> configure run
  -> calculate
  -> inspect diagnostics
  -> inspect demand/campaign/material/schedule
  -> compare to baseline
  -> accept/review
  -> freeze/release
  -> execute
  -> receive actuals
  -> replan remaining work
```

### 8.1 Input readiness

Before calculation, show a compact readiness gate:

- demand loaded
- inventory timestamp / trust
- resource/calendars loaded
- route/master validity
- planning horizon
- scenario overrides
- known blocking master-data errors

Do not make users discover a missing master only after a long solver run when it can be validated earlier.

### 8.2 Planning run

Planning runs are asynchronous jobs.

Suggested visible stages:

```text
Queued
Preparing input
Demand / supply netting
Campaign / heat planning
Production structure
Finite scheduling
Diagnostics
Persisting Plan Version
Complete / Failed / Cancelled
```

The UI shows stage, elapsed state, cancellation availability and failure reason. It must never pretend the browser itself performs the calculation.

### 8.3 Review

A feasible plan is not automatically an accepted plan.

Review surface should summarize:

- demand service
- late orders
- campaign decisions
- major transitions
- material assumptions
- external supply usage
- bottlenecks
- changes from baseline
- warnings / degraded assumptions

### 8.4 Release

Release is an auditable state transition. Show exactly what will be released:

- WOs
- operation groups
- resources
- quantities
- PO/SO allocations
- frozen horizon implications

---

## 9. Control Tower

The Control Tower is not a card dashboard. It is a plan-health instrument panel.

### Primary composition

#### Plan pulse strip

Horizontal strip for:

- feasibility
- review/release state
- service attainment
- plan churn
- material confidence
- critical diagnostics

#### Demand coverage composition

One integrated stacked view:

```text
Requested demand
| FG stock | existing billet | external billet | fresh production | uncovered |
```

Drill to affected SO/PO rows.

#### Resource pressure skyline

Physical resources, not only process families.

Shows:

- finite utilization
- blocked/downtime spans
- queue pressure
- next overload/bottleneck

#### Risk stream

Chronological list of events likely to affect the plan:

- late demand
- resource outage
- incoming billet risk
- material hold
- thermal/queue risk
- near-term operation without margin

#### Plan delta

Compared with the selected baseline:

- operations moved
- resource changes
- campaigns split/merged
- new/removed work
- service gained/lost

Every aggregate is clickable and highlights its contributing entities.

---

## 10. Demand & Supply workspace

The primary question is **how each demand requirement will be satisfied**.

### Main visualization

Demand rows use a quantitative coverage bar:

```text
SO / PO required quantity
[FG][existing billet][external billet][fresh SMS][uncovered]
```

### Supporting columns

- SO / item
- customer
- material / grade / final section
- required quantity
- due date
- priority
- MTO/MTS
- customer/quality restriction indicator
- target / projected stock for MTS
- plan completion
- service risk

### Inspector

Shows the immutable requirement snapshot used by the selected plan, including customer-specific narrowing rules.

### Core actions

- inspect requirement
- inspect supply allocation
- inspect why uncovered
- open assigned campaign
- open CTP for new/additional demand

Direct editing of ERP demand is not a planning UI concern unless explicitly supported by integration policy.

---

## 11. Campaign Studio

Campaign Studio explains and, where policy allows, constrains campaign formation.

### 11.1 Campaign composition map

Rows = POs.
Columns = selected campaigns.
Cell = allocated quantity.

This makes many-to-many split/merge behavior explicit.

### 11.2 Campaign ribbon

For selected campaign:

```text
PO allocations
 -> grade order
 -> heat structure
 -> cast section
 -> rolling requirements
```

### 11.3 Grade sequence visualization

Ordered grade nodes with transition edges showing:

- allowed / forbidden
- exact/class/family rule source
- transition severity
- sequence break

### 11.4 Heat structure

Each heat shows:

- planned input/output quantity
- furnace envelope
- grade
- heat-to-PO allocation
- special requirements
- downstream cast eligibility

### 11.5 Manual planning interaction

Do not allow a planner to directly mutate solver truth.

Manual interaction should create **constraints / preferences for the next run**:

- keep together
- keep separate
- force campaign
- forbid campaign
- preferred sequence
- freeze selected campaign

Then revalidate/reoptimize.

---

## 12. Steelmaking & Casting workspace

This view is heat-centric and process-centric.

### Heat train

A selected heat is visualized as a real process path:

```text
EAF -> LRF -> VD? -> CCM
```

Each operation node shows:

- eligible resource count
- selected physical resource
- duration
- planned start/end
- queue/transfer window
- requirement status
- execution status if released

### Cast sequence board

Each physical CCM is its own lane.

Within a sequence show:

- tundish / sequence identity
- heat order
- grade transitions
- section
- planned cast output
- sequence break reason

### Four-strand view

For a selected heat/cast:

```text
CCM-1
  Strand 1  planned output
  Strand 2  planned output
  Strand 3  planned output
  Strand 4  planned output
```

Later billet-piece/cut-pattern detail can expand beneath these lanes without changing the information architecture.

### Thermal overlay

A compact temperature/holding visualization may show:

- required entry/exit/casting ranges
- transfer loss assumptions
- maximum hold window
- planned queue margin

Do not imply a thermodynamic simulation where only configured envelopes exist.

---

## 13. Rolling & Finishing workspace

Primary question: **what feed reaches which mill, through what route, and what finished units result?**

### Feed map

For each rolling requirement show source path:

```text
fresh hot cast -> direct mill
existing billet -> RHF -> mill
external billet -> RHF -> mill
inventory billet -> mill where permitted
```

### Shared RHF visualization

One lane per physical RHF. Show contention from both mill streams and queue consequences.

### RM lanes

RM-1 and RM-2 are never visually merged.

Each block shows:

- grade
- input section -> output section
- source campaign / POs
- quantity
- changeover
- due/service signal

### Downstream route

TMT / cooling / cutting / bundling / coiling / finishing appear as process operations when present.

### Planned unitization

Show expected bundle/coil count/weights and packaging specification as planning output. Actual individual IDs belong to execution/genealogy.

---

## 14. Finite Schedule Board

This is the most information-dense planner screen.

### Required behavior

- horizontal time axis
- one lane per physical ResourceId
- virtualized rendering for large task counts
- synchronized frozen left resource column
- zoom day/shift/hour
- horizon and now/reference markers
- frozen/slushy/liquid background zones
- resource calendar/downtime blocks
- task blocks with process semantics
- setup/changeover spans or edge markers
- material-ready and due markers
- dependency/transfer paths on selection

### Selection behavior

Selecting one task highlights:

- predecessor/successor chain
- source heat/campaign/PO
- material receipt/consumption
- resource alternatives if retained in diagnostics
- related issue(s)

### Visual warnings

Warnings are compact overlays, not text inside every block:

- late
- thermal margin low
- material tight
- queue near max
- frozen
- actual deviates from plan

### Dragging

Do not make freehand Gantt dragging the authoritative scheduler.

If manual scheduling is introduced, a drag creates a proposed constraint or move and then runs validation/reoptimization. The UI must show whether the proposed move is accepted, rejected or causes downstream consequences.

---

## 15. Material Flow workspace

### 15.1 Time-phased availability

Use a step/area chart for each selected qualified material pool:

- opening inventory
- external receipts
- internal cast receipts
- reservations
- planned consumption
- actual adjustments

Zero is a hard visual floor. Any shortage region is explicit.

### 15.2 Source-to-demand flow

Use a Sankey or equivalent flow diagram selectively:

```text
FG stock / billet inventory / external supply / fresh casting
      -> PO / campaign requirements
      -> RHF / rolling
      -> FG
```

### 15.3 Reservation table

Shows exact ownership:

- source
- material/spec
- lot when known
- quantity
- available time
- PO
- campaign
- Plan Version
- reservation/release state

### 15.4 Quality/status

Held/blocked/rejected material is visible but visually excluded from usable supply.

---

## 16. Execution workspace

### 16.1 Work Orders & operations

WO is the execution container. Process operation remains the scheduling truth.

A single screen uses expandable rows:

```text
WO Steelmaking H101
  EAF operation
  LRF operation
  VD operation

WO Casting H101
  CCM operation
```

and similarly for RHF/RM/finishing.

### 16.2 Manual actual entry

Initial manual workflow must capture:

- status
- actual resource
- actual start/end
- actual quantity
- comment/reason
- produced material units/lots where applicable
- provenance = Manual

Corrections require explicit correction state/history.

### 16.3 Actual-vs-plan

Timeline overlay shows planned and actual spans without destroying the baseline plan.

### 16.4 Replan preview

Before replanning show:

- completed/fixed operations
- running operations
- frozen operations
- current inventory snapshot
- major deviations triggering replan

Then create a new Plan Version and compare it with the baseline.

---

## 17. Traceability workspace

Two synchronized modes.

### Commercial lineage

```text
SO/item -> PO -> Campaign allocation -> WO/operation allocation
```

### Physical genealogy

```text
Heat -> cast -> strand -> billet lot -> RHF/RM -> rolled lot -> bundle/coil -> FG
```

The graph should support forward/backward expansion without rendering the entire plant genealogy at once.

Each edge displays quantity where meaningful.

Planned pegging, current reservation and immutable actual genealogy must use different visual semantics.

---

## 18. Scenario / Compare workspace

### Scenario register

Show:

- name
- baseline
- override type
- resources/supply affected
- status
- owner / created time

### Compare view

Use synchronized columns or overlay views for:

- service
- campaigns/heats
- resource assignment
- operation movement
- material reservation
- bottlenecks
- diagnostics

Prioritize **delta** over displaying two entire plans independently.

### Scenario promotion

A scenario remains non-authoritative until explicitly promoted/recalculated/accepted according to application policy.

---

## 19. CTP / Promise workspace

CTP uses the same planning kernel and therefore should feel like a compact focused scenario.

Input:

- material / grade / section
- quantity
- requested date
- customer/quality constraints when relevant

Output should explain alternatives:

1. stock-only
2. join existing campaign
3. new campaign
4. earliest later date
5. cannot promise

Each answer displays:

- promise date
- planning basis
- material source
- campaign action
- required resource path
- risk/confidence/trust
- blocker if not feasible

No standalone green/red answer without explanation.

---

## 20. Diagnostics workspace

All failures/warnings should normalize into one diagnostic model.

Fields:

- severity
- hard/soft
- category
- code
- affected entity
- message
- evidence
- consequence
- suggested next action
- source plan version

Categories include:

- master data
- campaign compatibility
- furnace/heat size
- metallurgy/process
- thermal
- CCM/cast sequence
- material
- RHF/rolling
- capacity
- transition
- frozen/stability
- execution/integration

A diagnostic should link directly to the related entity or master editor.

---

## 21. Capacity workspace

Keep two lenses visually and semantically separate.

### Rough-cut

Fast planning estimate:

- demand hours
- available hours
- utilization
- overload

### Finite occupancy

From the actual finite schedule:

- scheduled process occupancy
- setup/changeover occupancy
- downtime
- idle
- starvation/wait

The UI must always label the capacity basis.

---

## 22. Master-data workbench

Master data is not a collection of unrestricted grids.

### Tree / relationship driven editors

Plant:

```text
Plant -> Area -> Stage -> Resource -> Capability -> Calendar
                           |
                           -> Flow Links
```

Grade:

```text
Grade -> Family / Sequence Class / Casting Class
      -> Chemistry
      -> Process requirements
      -> Thermal requirements
```

Product/material:

```text
Material -> Product form -> Cross section -> Packaging
```

Route:

```text
Route -> Operations -> Resource capabilities -> transfer/buffer semantics
```

Transition rules need an **effective rule preview** so users can see exact vs class/family vs default precedence.

Before saving a high-impact change, show validation and affected objects where technically possible.

---

## 23. Visual design language

The UI should read as a **precision industrial planning instrument**, not a generic SaaS admin dashboard.

### Principles

- dense but calm
- strong hierarchy through scale, alignment, surface depth and data shape
- subtle physical depth: inset tracks, raised controls, engraved/divided rails; avoid playful skeuomorphism
- sans-serif typography only
- compact numeric typography with tabular numerals
- whitespace is structural, not decorative
- avoid large rounded cards everywhere
- use continuous work surfaces, split panes and instrument strips

### Color semantics

Color must be consistent and never the only status carrier.

Suggested process semantics can remain stable across views, for example:

- EAF: hot orange
- LRF: violet
- VD: teal
- CCM: green
- RHF: amber
- RM: steel/blue-grey
- finishing: blue/cyan family

Status semantics are separate:

- feasible / completed
- warning / at risk
- error / blocked
- informational
- frozen / actual

Do not overload one color with both equipment and severity meanings.

---

## 24. Visualization policy

Prefer a chart/diagram when it answers a planner question faster than reading rows.

Recommended primary visuals:

- demand coverage stacked bars
- campaign allocation matrix
- grade transition sequence graph
- heat structure ribbon
- heat process train
- four-strand CCM diagram
- resource Gantt
- material time-phased step chart
- source-to-demand Sankey
- utilization skyline / heatmap
- plan delta movement visualization
- genealogy graph

Tables remain essential for exact values, filtering, export and bulk review.

---

## 25. Status model

Canonical UI lifecycle vocabulary must be centralized.

### Plan Version

```text
Queued
Preparing
Planning
Feasible
Infeasible
Review Required
Accepted
Frozen
Released
Superseded
Failed
Cancelled
```

Only use states actually supported by backend contracts; unsupported desired states must remain roadmap items until implemented.

### Operation / WO

At minimum align with application execution status rather than inventing UI-only states.

### Material

Differentiate:

- usable
- reserved
- in transit
- held
- blocked
- rejected
- consumed

---

## 26. Interaction safety rules

1. Never silently loosen a hard requirement.
2. Never let a UI drag or inline edit overwrite authoritative schedule truth without validation.
3. Never release an infeasible plan.
4. Never hide stale inventory or stale Plan Version context.
5. Never merge planned pegging, reservations and actual genealogy into one ambiguous relationship.
6. Never represent rough-cut capacity as finite schedule truth.
7. Never infer resource interchangeability from equipment type.
8. Destructive/high-impact master changes require explicit save and validation.

---

## 27. UI-enabling application/API requirements

Before production pages are built, create query/read contracts for:

- active/current Plan Version
- plan version register/detail
- planning run status/progress
- demand / PO coverage
- campaign detail and heat allocations
- process operations / schedule
- resource timeline/calendars
- material ledger/reservations
- diagnostics
- release/WO/operation state
- execution actuals
- genealogy
- compare
- CTP
- scenarios
- master list/detail/effective-rule/validation

Blazor should consume these contracts. It should not query `ApsDbContext` directly and should not reconstruct relationships from low-level endpoint fragments.

SignalR is appropriate for planning-run progress and live execution/replan refresh where useful.

---

## 28. Performance requirements

Design for real planning scale from the start.

- virtualize large grids
- virtualize/clip Gantt lanes
- use server-side paging/filtering for large registers
- downsample dense charts
- lazy-load inspector details
- render genealogy incrementally
- avoid sending entire Plan Versions to every page
- cache stable master reference data with explicit invalidation

The shell should remain responsive while a calculation is running.

---

## 29. Accessibility requirements

- keyboard navigation for all primary planner actions
- visible focus state
- status never depends on color alone
- tooltips are supplemental, not the only source of information
- sufficient contrast in dense schedule views
- reduced-motion support
- screen-reader names for action controls and chart summaries
- text alternatives for complex visualizations where practical

---

## 30. What happens to the current Planning Sandbox

The existing `/planning` sandbox remains useful as:

- engine smoke-test UI
- no-DB demonstration
- component/reference harness

It should **not** gradually become the production workspace.

Production pages should use DB-backed query/application contracts and the shared shell described above.

---

## 31. What happens to the old HTML prototype

The workbook-era HTML/CSS remains useful as a feature checklist and interaction reference for functions that existed there:

- planning
- execution
- material
- capacity
- CTP
- scenarios
- BOM/master data

It is not the information architecture or visual target for the new product.

The flat top-tab structure, repeated generic cards and locally computed presentation logic should not be carried forward wholesale.

---

## 32. Coverage rule

Every new backend capability is incomplete from a product perspective until this table can be answered:

| Question | Required answer |
|---|---|
| Where can I see it? | workspace / inspector |
| How do I understand why it exists? | lineage / diagnostics / rule source |
| How do I act on it? | safe explicit command or read-only by design |
| What plan/version does it belong to? | visible Plan Version context |
| Can I trace it upstream/downstream? | entity links / graph |
| Can I compare it with the previous plan? | delta support where meaningful |
| Is it authoritative or estimated? | explicit basis badge |

This coverage rule should be applied to every future domain feature.

---

## 33. Implementation tracking

Production UI work is tracked under Epic #20 and child issues #21-#31.

The intended order is:

1. #22 query/read-model layer
2. #21 shell/design system/inspector
3. #23 Control Tower / Plan Version lifecycle
4. #24 Demand & Campaign Studio
5. #25 physical production + finite schedule
6. #26 material flow
7. #27 execution/replan/traceability
8. #28 scenarios/compare/CTP/capacity
9. #29 diagnostics
10. #30 master-data workbench
11. #31 accessibility/performance/E2E/visual regression

The visual implementation should begin only after the UI data contracts for its first workspace are stable enough to avoid moving planning logic into Razor components.