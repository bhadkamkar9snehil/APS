# APS Production UI Implementation Plan

**Status:** current UI delivery plan — implemented foundation + remaining product work  
**Re-baselined:** 23-Aug-2026 against `main` at `71e456d2fe124173cdd1f0bfeac82e18f53dc45f`

The old version of this document described an early state in which `APS.UI` contained little more than Home/Planning sandbox pages. That is no longer true. Current `main` already contains a substantial production Blazor planner and the large Gantt workbench overhaul.

Current implementation-state authority: [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md).

---

## 1. Objective

Complete the production planner interface without moving planning truth into Razor/JavaScript and without rebuilding backend logic per screen.

The UI consumes typed application/query contracts. It may own:

- selection;
- filtering;
- viewport/zoom/pan;
- visual preferences;
- staged interaction state;
- formatting.

It must not own:

- material balance;
- resource eligibility;
- campaign compatibility;
- service/lateness truth;
- thermal decisions;
- release readiness;
- solver feasibility;
- authoritative Plan Version mutation.

---

## 2. Current implemented UI baseline

Current `APS.UI/Components/Pages` includes production-facing pages such as:

- `Home.razor`;
- `DemandSupply.razor`;
- `CampaignStudio.razor`;
- `SteelmakingCasting.razor`;
- `RollingFinishing.razor`;
- `FiniteSchedule.razor`;
- `MaterialFlow.razor`;
- `Inventory.razor`;
- `PlanVersions.razor`;
- `PlanCompare.razor`;
- `MasterData.razor`;
- `Planning.razor` for the deliberately separate demo/sandbox path;
- execution/traceability/decision pages present in the same production UI tree.

The product is therefore **not** waiting for “Phase 1 shell before any production UI can exist.” The remaining work is to deepen authoritative contracts, workflows and acceptance quality around an already substantial UI.

---

## 3. Current UI architecture

```text
APS.Domain
  domain identities/rules

APS.Application
  commands + query/read contracts + lifecycle contracts

APS.Infrastructure
  persistence/providers/query implementations

APS.Service
  API/host/composition

APS.UI
  shell
  domain workspaces
  Gantt/workbench
  client interaction/view state
  formatting/visualization

APS.DesktopHost
  Windows desktop hosting/update/runtime integration
```

Non-negotiable rules:

- no EF `DbContext` in Razor components;
- no solver/material recomputation in UI;
- no direct unsafe mutation of Campaign/Heat/resource truth;
- UI edits become validated commands/replans/child Plan Versions;
- stable backend IDs/planning keys support cross-view navigation;
- read models should expose reasons/basis so UI does not parse prose or infer domain decisions.

---

## 4. Shell/design-system status

A production shell/design system already exists and has gone through multiple simplification passes.

Post-Ponytail cleanup removed several wrapper components/types where they no longer earned their abstraction cost. Examples include old `NavGroup`, `NavItem`, `PlanContextBar`, `AppearancePopover` and theme preference/accent/color helper types. Their deletion should not be interpreted as deleting the whole navigation/theme experience; current ownership was consolidated into fewer shell/theme components/services.

Current direction remains:

- dense industrial workspace;
- continuous work surfaces rather than generic SaaS card grids;
- compact but legible typography;
- restrained depth/elevation;
- semantic status colors separated from process/equipment colors;
- stable geometry for high-density planner use;
- keyboard/accessibility support as a product requirement, not an afterthought.

Future UI cleanup should optimize ownership/clarity while preserving user-observable behavior.

---

## 5. Gantt / finite schedule — implemented central workbench

The finite schedule is no longer a “basic Gantt sandbox.” A large custom workbench is integrated.

Implemented foundations include:

- synchronized resource grid + UTC timeline;
- hierarchical physical-resource lanes;
- row/time virtualization;
- resource calendars/capacity;
- operation blocks and inspector;
- baseline comparison;
- campaign spans;
- dependency focus;
- Now/reference/frozen-fence markers;
- execution actual overlays;
- zoom/pan/fit/reset;
- splitters/resizable grid columns;
- keyboard navigation/accessibility contracts;
- multi-selection;
- staged single and atomic bulk moves;
- authoritative final-position bulk-move validation;
- frozen/time-fence enforcement;
- resource-load/capacity region;
- schedule overlays/auxiliary panels;
- released-baseline edit protection;
- pointer-cancel/blur cleanup.

See [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md).

### Gantt component consolidation

Standalone baseline/calendar/campaign/dependency/execution/marker/proposal layer files from the original overhaul branch were later consolidated into the current lane/viewport scene. The behavior remains; the file boundaries changed.

Do not create new wrapper components merely to recreate the old filename structure.

---

## 6. Plan lifecycle UI — current state

`PlanVersions.razor` now sits on top of the current release lifecycle, which includes a real persisted Approved state.

Required workflow semantics are:

```text
calculate/replan
 -> Feasible
 -> inspect persisted readiness
 -> Approve
 -> Release
```

UI must show readiness blockers from the backend contract. It must never enable a direct Feasible -> Released shortcut or reimplement release readiness client-side.

Current readiness includes material/supply and persisted MTO service evidence. Future service-date refinement must flow from backend contracts rather than Razor logic.

---

## 7. Current workspace status and remaining focus

### Demand & Supply

Implemented page foundation exists. Remaining work is driven by backend visibility/completeness rather than creating the page from scratch:

- allocation-grain service/date semantics;
- richer supply/shortfall basis;
- cross-navigation into material/campaign/schedule;
- complete typed exposure under #36.

### Campaign Studio

Implemented page foundation exists. Continue to expose:

- PO allocations and quantity identity;
- grade sequence/heat structure;
- service implications;
- candidate/rejection/transition evidence as backend #19/#36 surfaces mature;
- planning constraints/replan actions rather than direct mutation.

### Steelmaking / Casting

Implemented page foundation exists. Continue to expose actual route-driven operations rather than a fixed EAF/LRF/VD diagram. Resource alternatives/commitment/actual resource should deepen as #16 lands.

### Rolling / Finishing

Implemented page foundation exists. #56 now provides time/temperature-aware billet thermal decisions; UI should consume and expose hot/reheat basis rather than treating #56 as future work.

### Material Flow / Inventory

Implemented page foundations exist. Continue toward full requirement -> coverage -> reservation -> supply -> shortfall drilldown directly from canonical material facts.

### Plan Compare / decision surfaces

Operation comparison exists. #57/#43 remain backend owners for broader scenario/service/material/campaign/capacity/diagnostic comparison and CTP/capacity convergence. UI should extend the existing compare path, not build a second scenario-only truth engine.

### Execution / traceability

Page/read foundations exist, but #18 remains the key backend gap for full physical material transformation/genealogy and actual-state closure.

### Master Data

A substantial MasterData page exists. #60/#39/#41 remain the backend/application owners for typed validated operational authoring/effective-value semantics. Do not compensate with direct EF access from UI.

---

## 8. Workspace state

The original plan proposed a broad `PlannerWorkspaceState`. Ponytail cleanup intentionally reduced this to state that genuinely needs cross-component persistence.

Current design rule:

- Plan Version/baseline identity and real shared selection/view state may live in scoped state;
- transient component state stays with the owning component;
- no-op compatibility setters are not evidence that a broad global state object should be rebuilt;
- URL/deep-link state should be added only where it materially improves navigation/bookmarking.

Do not resurrect global UI state simply for symmetry with the original plan.

---

## 9. Remaining delivery sequence

The UI program is now dependency-driven around remaining backend truth rather than old numbered “build the first page” phases.

### A. #16 resource commitment/redispatch UI enablement

Once the generic backend lifecycle lands, expose:

- eligible alternatives and exclusion reasons;
- planned/committed/actual resource;
- commitment state/policy;
- redispatch preview/result/history;
- local repair impact.

The finite schedule/workbench is the primary operational surface for this, but backend commands own mutation.

### B. #18 execution/genealogy

Deepen Work Orders/operations/actuals/traceability around:

- actual resource/time/quantity;
- consumed/produced material;
- split/merge genealogy;
- external billet downstream genealogy;
- plan versus actual variance;
- replan impact.

### C. #19 diagnostics

Upgrade analysis/diagnostic surfaces from summary/navigation to stable domain-coded evidence:

- hard versus soft;
- entity references;
- material/route/resource/thermal/capacity/time-fence categories;
- safe advisory restoration guidance;
- objective/penalty evidence where available.

### D. #57 / #43 decision workbenches

Extend existing Plan Compare/scenario/capacity surfaces using canonical persisted facts. CTP/scenario/capacity must never call a hidden alternate planner.

### E. #36 complete read/command exposure

Close any remaining UI need to parse opaque JSON, join unrelated DTOs or infer backend decisions.

### F. #60 master authoring

Make the existing master workbench fully operational with typed validation/effective-value/impact feedback.

### G. #61 realistic reference data

Use the deterministic persisted reference plant to design/test realistic information density, not only tiny fixtures.

### H. #31 systematic browser/visual/E2E quality

Build repeatable coverage for:

- pointer drag/autoscroll/pan;
- focus and virtualization;
- fullscreen/localStorage;
- long-open-session time progression;
- 1080p/1440p/4K layouts;
- visual regression;
- end-to-end plan -> diagnose -> compare -> approve -> release;
- execution -> replan;
- CTP/scenario/traceability/master-validation journeys.

---

## 10. Key entity inspector contract

Continue to expose authoritative facts for:

| Entity | Primary facts |
|---|---|
| Sales Order | customer/item/qty/service date/requirements/coverage/PO lineage |
| Production Order | MTO/MTS source, manufacturing qty, FG coverage, campaign/material/service status |
| Campaign | PO allocations, grade sequence, heat structure, service/transition/candidate evidence |
| Heat | grade/quantity/PO allocations/route/cast sequence/thermal/resource state |
| Operation | process, eligible/planned/committed/actual resource, planned/actual time, constraints, baseline/binding/material evidence |
| Resource | capability/state/calendar/scheduling mode/capacity/load/upcoming work |
| Material requirement/supply | required qty/time, coverage/reservation, supply basis, shortfall/late state |
| Material lot | physical genealogy/quality/location/status/commercial allocations where applicable |
| Work Order | allocations, process operations, plan/actual, external references |
| Diagnostic | stable code, severity, hard/soft, evidence, entity refs, advisory suggestion |
| Plan Version | status, baseline/scenario, horizon, assumptions/readiness/comparison/release audit |

Do not duplicate entity presentation/meaning per page.

---

## 11. UI product quality definition

A UI capability is not complete because a route renders.

For each workflow require:

- authoritative typed backend source;
- explicit loading/empty/error/stale state;
- reason/source for important status;
- stable cross-navigation IDs;
- keyboard/focus behavior where applicable;
- no color-only critical meaning;
- realistic-density performance;
- correct historical Plan Version interpretation;
- backend-validated state-changing commands;
- component/model regression coverage;
- browser/desktop workflow evidence where browser behavior matters;
- exact-SHA Windows verification before claiming release readiness.

---

## 12. Current highest-value UI work

Do **not** restart the shell or rebuild the Gantt from the old implementation plan.

Highest-value UI work is now:

1. preserve and harden the integrated Gantt/workbench;
2. wire #16 resource commitment/redispatch facts/commands into that workbench once backend-authoritative;
3. deepen execution/genealogy/diagnostic/decision workspaces as #18/#19/#57/#43/#36 land;
4. complete master authoring with #60;
5. use #61 realistic data plus #31 browser/visual/E2E testing to finish production quality.

The production UI already exists. The task is now **domain completeness, interaction correctness and operational quality**, not first-page construction.
