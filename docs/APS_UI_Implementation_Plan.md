# APS Production UI Implementation Plan

## 1. Objective

Implement the complete production Blazor interface described in `APS_UI_UX_Product_Blueprint.md` without moving planning logic into the UI and without building screens ahead of authoritative backend/query support.

The plan is organized by dependency, not by visual convenience.

---

## 2. Current baseline

### Current production-code strengths available to the UI

PR #1 already establishes useful application/domain foundations including:

- MTO Production Orders with SO/item lineage
- MTS Production Orders from stock policy
- FG/intermediate inventory netting
- campaign allocations
- heat-level planning structure
- caster/mill structure
- finite CP-SAT schedule
- physical-resource calendars
- plan version persistence / baseline comparison concepts
- Work Order release
- execution updates and traceability contracts
- inventory snapshot provider
- planning/replanning APIs

### Current UI

`APS.UI` currently contains only:

- `Home.razor`
- `Planning.razor`
- shared schedule visualization support

`Planning.razor` is explicitly a built-in-sample Planning Sandbox. It is not DB-backed planner UX.

### Consequence

The first production UI implementation step is **not** styling. It is the production read/query layer and workspace state model.

---

## 3. Architecture for the UI

Recommended project responsibilities:

```text
APS.Domain
  canonical entities / rules

APS.Application
  commands
  query contracts
  read models
  lifecycle policy
  validation

APS.Infrastructure
  EF/query implementations

APS.Service
  API endpoints
  SignalR planning/execution notifications
  composition root

APS.UI
  shell
  pages
  visualization components
  client/workspace state
  formatting only
```

Rules:

- no EF DbContext in Razor components
- no solver invocation from presentation components except through application/service command boundary
- no material balance / lateness / utilization recomputation in JavaScript or Razor
- charts receive already-defined semantic read models
- UI state may contain filters/selection/view preferences, never planning truth

---

## 4. UI read-model strategy

Avoid one giant `PlanningRunResult` DTO for all pages.

Create workspace-specific read models.

Suggested contracts:

```text
PlanContextVm
ControlTowerVm
DemandCoverageVm
ProductionOrderDetailVm
CampaignStudioVm
HeatDetailVm
CastSequenceVm
ResourceScheduleVm
MaterialFlowVm
WorkOrderExecutionVm
GenealogyVm
PlanCompareVm
ScenarioVm
CtpResultVm
DiagnosticRegisterVm
MasterImpactVm
```

Each model must include:

- PlanVersionId when plan-bound
- authoritative/estimated basis
- timestamps/data freshness where relevant
- stable entity IDs for cross-navigation
- human-readable code/number fields
- reason/status fields supplied by application layer

---

## 5. Workspace state model

Create a scoped UI service similar to:

```text
PlannerWorkspaceState
  CurrentPlanVersionId
  BaselinePlanVersionId
  ScenarioId
  Horizon
  SelectedEntity
  SelectedResourceIds
  TimeWindow
  GlobalFilters
```

Selection is an entity reference:

```text
EntityRef
  EntityType
  EntityId
  DisplayCode
```

This enables a selected Campaign or Heat to remain selected when the user moves between Campaign Studio, finite schedule and material flow.

State that matters for deep links should also be representable in URL/query parameters.

---

## 6. Phase 0 - backend/UI contract audit

Issue: #22

Before building production screens, inventory current backend commands/queries/API endpoints against the blueprint.

Deliverable: a coverage table with statuses:

- Ready
- Partial
- Missing

At minimum audit:

| Capability | Required UI contract |
|---|---|
| Plan current/list/detail | query |
| Planning run start/status | command + progress query/event |
| Demand/PO register | query |
| Demand coverage | query |
| Campaign list/detail | query |
| Heat allocations | query |
| Cast sequence | query |
| Schedule/resource lanes | query |
| Material reservations/events | query |
| Diagnostics | query |
| Plan compare | query |
| Release preview/commit | command |
| WO/operation execution | query + command |
| Replan preview/run | query + command |
| Traceability | query |
| Scenarios | query + command |
| CTP | command/query result |
| Capacity | query |
| Master data | CRUD/query/validation |

Do not begin a production page whose essential contract is `Missing`.

---

## 7. Phase 1 - shell and design system

Issues: #21, #31

### Components

```text
AppShell
PlanContextBar
PrimaryRail
WorkspaceHeader
CommandBar
ContextInspector
InspectorSection
StatusPill
BasisBadge
EntityLink
MetricStrip
SplitPane
DataGrid
EmptyState
ErrorState
ProgressStage
DiagnosticBadge
```

### Design tokens

Create one design-token source for:

- surface hierarchy
- border/depth
- typography
- spacing
- process colors
- status colors
- motion
- chart grid/axis styling

Prefer CSS custom properties backed by component classes; Tailwind may be used for composition but must not fragment semantic styles across pages.

### First acceptance target

A static shell populated with fake/read-only `PlanContextVm` should already demonstrate:

- persistent plan context
- rail
- split workspace
- inspector
- compact dense visual hierarchy

No feature-specific production behavior yet.

---

## 8. Phase 2 - Control Tower / Plan lifecycle

Issue: #23

### Required queries

```text
GET current plan context
GET plan versions
GET control tower summary
GET diagnostics summary
GET plan delta summary
```

### Required commands

```text
StartPlanningRun
CancelPlanningRun
AcceptPlan
FreezePlan
ReleasePlan
StartReplan
```

Only implement commands actually supported by backend lifecycle. Missing lifecycle states should first be added to Application/Domain.

### Visual components

- PlanPulse
- DemandCoverageSummary
- ResourcePressureSkyline
- RiskStream
- PlanDeltaSummary
- PlanningRunProgress

### Completion criterion

A planner can enter the app, identify the current plan, know if it is usable and navigate directly to the reason it is not.

---

## 9. Phase 3 - Demand & Campaign Studio

Issue: #24

### Pages/routes

```text
/plan/demand
/plan/campaigns
/plan/campaigns/{id}
```

### Demand read model

For every demand/PO row expose:

- SO/item/customer
- MTO/MTS
- requested quantity
- due/priority
- grade/material/final section
- requirement snapshot flags
- FG coverage
- intermediate coverage
- external supply coverage
- fresh requirement
- uncovered quantity
- planned completion
- service risk

### Campaign read model

Expose:

- campaign identity/status
- PO allocation quantities
- grade sequence
- heat structure
- heat allocations
- selected section/route
- inventory/fresh split
- transition rules and reason/source
- diagnostics / rejected alternatives when available

### Interaction

Initial version is read/inspect + planning constraints.

Do **not** implement direct mutable drag/drop campaign truth.

If manual intervention is desired, add application commands such as:

```text
SetCampaignKeepTogetherConstraint
SetCampaignKeepSeparateConstraint
SetPreferredCampaignConstraint
FreezeCampaign
```

then rerun planning.

---

## 10. Phase 4 - physical process / finite schedule

Issue: #25

### Routes

```text
/plan/steelmaking
/plan/rolling
/plan/schedule
```

These can share the same selected entity/time window state.

### ECharts usage

Use ECharts for:

- compact process timelines
- thermal/queue margin plots
- utilization views
- material readiness overlays

### Custom schedule board

The finite Gantt may require a custom HTML/CSS/Canvas/SVG hybrid rather than forcing ECharts into an operational scheduler.

Requirements:

- virtualized resource lanes
- independent physical ResourceId lanes
- task blocks
- downtime blocks
- frozen/slushy/liquid zones
- dependency overlays only for selected task
- zoom/pan
- now/reference marker
- hover/selection linked to inspector

### First performance target

Design for at least:

- hundreds of heats
- thousands of operation blocks
- dozens of resources

without DOM creation proportional to every possible dependency edge.

---

## 11. Phase 5 - Material Flow

Issue: #26

### Routes

```text
/plan/material
/material/inventory
/material/supply/{id}
```

### Components

- MaterialAvailabilityChart
- MaterialFlowSankey
- ReservationRegister
- SupplySourceBadge
- MaterialStatusBadge
- MaterialTimelineInspector

### Contract requirement

The backend/read model must expose the actual planning material events/reservations. Do not reconstruct them by subtracting campaign quantities in the UI.

---

## 12. Phase 6 - Execution / Replan / Traceability

Issue: #27

### Routes

```text
/operate
/operate/work-orders
/operate/work-orders/{id}
/operate/replan
/trace
/trace/{entityType}/{id}
```

### Work Order page

Use a hierarchical operation timeline, not one flat WO grid.

```text
WO
  allocation summary
  operation 1
  operation 2
  operation 3
  produced/consumed material
  actual history
```

### Manual actual entry

Use a focused right-side command panel.

Every submission shows:

- source = Manual
- timestamp
- correction status
- exact operation/WO target

### Replanning

The replan screen is a **diff-before-run** setup:

- what is completed
- what is running
- what is frozen
- current inventory timestamp
- new events/deviations
- selected baseline

After run, navigate directly to Plan Compare.

### Traceability

Use incremental graph expansion. Never render all genealogy nodes by default.

---

## 13. Phase 7 - Scenario / CTP / Diagnostics / Capacity

Issues: #28, #29

### Scenario routes

```text
/decide/scenarios
/decide/scenarios/{id}
/compare/{baseline}/{candidate}
```

### CTP route

```text
/decide/ctp
```

### Diagnostics route

```text
/diagnostics
```

### Capacity route

```text
/capacity
```

The user should be able to jump into these workbenches from any affected entity via the inspector.

---

## 14. Phase 8 - Master-data workbench

Issue: #30

### Routes

```text
/configure/plant
/configure/resources/{id}
/configure/grades
/configure/grades/{id}
/configure/materials
/configure/sections
/configure/routes
/configure/transitions
/configure/calendars
/configure/external-supply
```

### Editor pattern

Use three panes where useful:

```text
master list/tree | editor | effective result / impact
```

Example grade editor:

- identity/family/class
- chemistry ranges
- process requirements
- thermal requirements
- customer override compatibility preview
- effective resource eligibility preview

Example transition-rule editor:

- rule list
- exact/class/family/default scope
- precedence preview
- matrix/graph visualization only for the effective subset currently selected

Do not present a 350 x 350 editable grade matrix.

---

## 15. Entity inspector content matrix

| Entity | Key inspector content |
|---|---|
| Sales Order | customer/item/qty/due/requirement/coverage/PO |
| Production Order | MTO/MTS source, quantity, stock/fresh split, campaign allocations |
| Campaign | PO allocations, grades, heats, section, due, diagnostics |
| Heat | grade, quantity, PO allocations, process route, cast sequence |
| Operation | process, resource, time, dependencies, constraints, actuals |
| Cast Sequence | CCM, heats, section, tundish/sequence, strand output |
| Resource | capability, state, calendar, load, upcoming tasks |
| Material supply | source, spec, qty, availability, reservation |
| Material lot | genealogy, quality, current location/status, PO allocations |
| Work Order | allocations, operations, planned vs actual, external reference |
| Diagnostic | severity, hard/soft, evidence, affected entities, suggestion |
| Plan Version | baseline, status, horizon, solver, changes, audit |

---

## 16. Backend-to-UI coverage matrix

### Current PR #1 baseline

| Capability | Current backend status | Current production UI status | Target issue |
|---|---|---|---|
| Planning run | Ready/partial production API + real engine | sample sandbox only | #22/#23 |
| Campaigns | Ready | table in sandbox | #24 |
| Heat/cast planning | Ready in current core scope | partial table | #25 |
| Rolling plans | Ready in current core scope | partial table | #25 |
| Finite schedule | Ready | basic Gantt sandbox | #25 |
| Plan Versions | Ready/partial | none | #23 |
| Plan compare | Ready/partial | none | #28 |
| Inventory snapshot | Ready | none | #26 |
| Work Order release | Ready | preview table in sandbox | #27 |
| Work Order execution | Ready | none | #27 |
| Heat/strand actuals | Ready | none | #27 |
| Traceability | Ready/partial | none | #27 |
| CTP | workbook-era implementation exists; .NET production contract to audit | old HTML only | #22/#28 |
| Scenarios | roadmap / contract audit required | old HTML only | #22/#28 |
| Diagnostics | issue objects exist; production normalization/read model required | basic issue list in sandbox | #29 |
| Master-data editing | persistence exists for several masters; production CRUD/validation audit required | none | #22/#30 |

This table must be updated as backend work advances. UI completion claims should reference it.

---

## 17. Visual design implementation

### Do not copy the old CSS token-for-token

The old prototype establishes some useful process color ideas but visually relies heavily on:

- white cards
- rounded rectangles
- floating KPI tiles
- top tabs
- generic soft shadows

The production design should instead use:

- continuous work surfaces
- fixed rails
- split panes
- instrument strips
- recessed schedule/material tracks
- stronger typographic hierarchy
- deliberate numeric density
- subtle dimensional borders and depth

### Theme direction

Recommended first production direction:

**precision industrial light workspace**

- graphite navigation chassis
- warm off-white / steel work surface
- darker inset timelines and resource rails
- restrained violet/teal brand accent
- equipment/process colors only where semantically useful
- semantic warning/error colors protected from process-color collisions

A dark operational theme can be added later, but should share the same geometry and semantics rather than become a separate design system.

---

## 18. Component boundaries

Suggested component families:

```text
Shell/
  AppShell
  PlanContextBar
  PrimaryRail
  WorkspaceHeader
  ContextInspector

Planning/
  DemandCoverageBar
  CampaignAllocationMatrix
  GradeSequenceGraph
  HeatStructureRibbon
  HeatProcessTrain
  CastSequenceBoard
  StrandOutputView
  ResourceScheduleBoard

Material/
  AvailabilityChart
  MaterialFlowSankey
  ReservationRegister

Execution/
  WorkOrderTree
  OperationActualEditor
  PlanActualOverlay
  GenealogyExplorer

Decision/
  ScenarioDelta
  CtpDecisionPanel
  DiagnosticRegister
  CapacityLens

MasterData/
  MasterTree
  EffectiveRuleViewer
  MasterImpactPanel
```

Avoid page-specific copies of the same entity display logic.

---

## 19. Testing sequence

Every phase gets:

1. application/query unit tests
2. component tests
3. deterministic sample read-model fixture
4. screenshot/visual regression
5. browser E2E for primary flow

Key E2E journeys:

### Journey A - normal plan

```text
Control Tower
 -> Run Plan
 -> Campaign inspection
 -> Schedule inspection
 -> Compare
 -> Accept
 -> Release
```

### Journey B - infeasible

```text
Run Plan
 -> Infeasible
 -> Diagnostic
 -> affected PO/Heat/Resource
 -> master/action suggestion
```

### Journey C - execution/replan

```text
Released WO
 -> actual operation update
 -> actual material output
 -> Replan
 -> Compare against baseline
```

### Journey D - traceability

```text
Bundle/coil
 -> RM
 -> billet
 -> cast/strand/heat
 -> campaign
 -> PO
 -> SO
```

### Journey E - CTP

```text
request qty/date
 -> alternatives
 -> blocker or promise basis
 -> inspect implied campaign/resource/material path
```

---

## 20. Definition of done for a feature

A backend capability is product-complete only when:

- its authoritative state is queryable
- it has a deliberate UI location
- it has loading/empty/error/stale states
- its status/reason is understandable
- upstream/downstream lineage is navigable
- its important actions are explicit and auditable
- it participates in Plan Version context where applicable
- it has component/E2E coverage
- it meets performance/accessibility expectations

---

## 21. Delivery order

The recommended implementation sequence is strict:

```text
#22 Query/read model foundation
  -> #21 shell + inspector
  -> #23 Control Tower
  -> #24 Demand/Campaign
  -> #25 Physical production + finite schedule
  -> #26 Material
  -> #27 Execution/replan/trace
  -> #28 Scenario/Compare/CTP/Capacity
  -> #29 Diagnostics deepening
  -> #30 Masters
  -> #31 hardening
```

Some issues can overlap after #21/#22 establish stable foundations, but no feature page should bypass those foundations to move faster.

---

## 22. Immediate next implementation tranche

Before visual page construction:

1. inventory current Application/Service contracts against #22
2. create missing query/read-model contracts for Control Tower and Plan context
3. establish `PlannerWorkspaceState`
4. implement the production shell and inspector with sample read-model fixtures
5. connect shell to real Plan Version/current-plan queries
6. build Control Tower first

Only then begin Campaign Studio and the operational schedule board.

This gives the product a stable navigation/state/data foundation and prevents a second UI rewrite after the domain grows.