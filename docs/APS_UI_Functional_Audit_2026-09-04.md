# APS UI Functional Audit — 2026-09-04

**Status:** live findings, verified against running code — not a historical/superseded document.
**Scope:** `src/APS.UI` as served by `src/APS.Service` on branch `codex/ui-workbench-chrome-legibility` at commit `d8be71a`.
**Method:** every finding below was verified by running `APS.Service` locally and driving the actual browser UI (clicking buttons, filling forms, reading server logs/console) — not by reading source alone. Source citations are given for each finding so they can be re-checked directly. Items not live-verified are marked as such.

This audit does not supersede [`current/APS_CURRENT_STATE_2026-08-23.md`](current/APS_CURRENT_STATE_2026-08-23.md) or [`current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md`](current/APS_GANTT_OVERHAUL_IMPLEMENTATION_STATUS.md); it is a defect/verification log that should be read alongside them and retired into the backend/UI work program once its items are triaged.

## Summary

The core plan lifecycle (create → approve → release → execution) is real and persists correctly end to end; it is not a facade. Alongside that, this audit found one master-data bug that blocks creating any new feasible plan from the current seed data, one dead button, one invisible-UI CSS typo, an unreachable page, no destructive-action confirmation anywhere in Master Data, and two workbench summary panels that are permanent dead ends with no drill-through. Severities below reflect user-facing impact, not code complexity.

## Confirmed broken

### 1. Calculate cannot produce a new feasible plan — High — data/config, not UI
Every attempt to run **Calculate** (`/plan/versions`, [PlanVersions.razor:165-193](../src/APS.UI/Components/Pages/PlanVersions.razor)) fails with the same domain error regardless of horizon:

> "No technically feasible campaign composition exists for this compatibility group. No sequence of technically feasible campaign segments covers all requirements. | Configured route STD-BAR ends candidate projection at BLT-150SQ but MTO-SO-1001-10 requires RND-12."

Tested with the demo default horizon and with the horizon matching the existing successful plans (20–31 Aug) — both fail identically. Root cause: sales order `MTO-SO-1001-10` requires cross-section `RND-12`, but its configured manufacturing route `STD-BAR` only produces up to `BLT-150SQ`. This is a seed master-data inconsistency (route/grade mismatch), not a defect in the Calculate wiring — the solver is correctly refusing infeasible input. **Impact: a planner cannot create any new plan version from the current demo dataset.** Existing plan versions in the register were created before this data issue (or under different config) and remain usable.

### 2. "Reconcile Sales Orders" button is dead — High
[DemandSupply.razor:14](../src/APS.UI/Components/Pages/DemandSupply.razor#L14), handler `ToggleReconcileForm()` at line 203. Clicking the button should flip `showReconcileForm` and reveal the inline reconcile form (lines 19+). It does not. Verified three independent ways: normal click via automation, precise-coordinate click, and a raw `btn.click()` dispatched directly on the DOM node from the console — none opened the form. No JS console error, no server-side exception logged. Confirmed isolated to this button: selection/interaction on the same circuit works correctly elsewhere (e.g. Campaign Studio row selection re-renders correctly).

### 3. Invisible shortfall indicator — Medium — one-character CSS typo
[DemandSupply.razor:114](../src/APS.UI/Components/Pages/DemandSupply.razor#L114): `@CoverageSegment(row.UncoveredQuantityMt, row.RemainingQuantityMt, "bg-danger-soft0")` — trailing `0` typo. Every other usage in the codebase (17 call sites checked) correctly uses `bg-danger-soft`. Tailwind generates no class for the typo, so the "Uncovered" segment of the demand coverage bar renders with zero visible fill. Not exercised by the current demo dataset (0 MT uncovered everywhere), so it silently ships until the first real shortfall.

### 4. No confirmation on destructive Master Data actions — Medium
All 8 Master Data tabs (`/plan/master-data`: Plants, Process Stages, Resources, Steel Grades, Routes, Materials, Cross Sections, External Supply) use the same unguarded delete pattern. Live-tested: added a test plant, clicked **Delete**, it was removed immediately with no dialog, no undo. One misclick permanently removes a master-data row.

### 5. Control Tower page is unreachable — Medium
[Home.razor](../src/APS.UI/Components/Pages/Home.razor), route `/control-tower`. Fully built and functional (live plan footprint, resource pressure, material commitments, execution state, plan version history — verified by loading it directly). Confirmed live: opened the "Workspaces" menu and read all 10 entries (Planning Workbench, Plans and scenarios, Campaign register, Demand and supply, Inventory, Material flow, Work order register, Steelmaking and casting, Rolling and finishing, Traceability) — Control Tower is not among them, and no other page links to it.

### 6. Capacity and Delivery workbench summary tabs are permanent dead ends — Low/Medium
[WorkbenchAnalysisDock.razor:79-84](../src/APS.UI/Components/PlanningWorkbench/WorkbenchAnalysisDock.razor#L79): the `Href`/`LinkLabel` constructor arguments are omitted for `Capacity` and `Delivery`, while `Material`, `ScenarioComparison`, `Execution`, and `Traceability` all link out to a full page (`/plan/material-flow`, `/decide/compare`, `/operate/work-orders`, `/operate/traceability`). No standalone Capacity or Delivery page exists anywhere in the routed pages. These two tabs are permanently capped at one static sentence and one number — there is no deeper view to reach, by design or by omission.

## Confirmed working (live-tested, not read-only inspection)

| Feature | Where | Evidence |
|---|---|---|
| Validate | Gantt command bar | Real feasibility check; correct pass banner |
| Optimize | Gantt command bar | Real CP-SAT solve; correctly reports the same route/grade infeasibility as #1 |
| Calculate → Approve → Release lifecycle | `/plan/versions` | Status persisted Feasible → Approved → Released; "Released 75 Work Order(s)" message matched exactly by the Work Orders register afterward |
| Release-lock | Gantt workbench | Optimize/Validate correctly disabled once plan is Released |
| Master Data Add | `/plan/master-data` | Added a test plant, persisted, then deleted |
| Plan Compare | `/decide/compare` | Real operation-level diff: 105 moved operations, exact resource/timestamp deltas |
| Traceability | `/operate/traceability` | Real filters (type/status/grade/date); master-detail drill-down from Work Order to Production Orders/Produced Lots works |
| Material Flow | `/plan/material-flow` | 15 real material pools, real ledger events tied to actual heat/plan GUIDs |
| Campaign Studio row selection | `/plan/campaigns` | Selecting a different campaign correctly re-renders grade order/heat structure/PO allocation |
| Workbench lifecycle mode tabs (Plan/Campaigns/Execution/Recovery) | Gantt workbench | Functional; the radio input is visually hidden (`sr-only`) with a `<label>` as the click surface — a real mouse click on the label works, this only affects raw-coordinate automation |

## Not yet verified

- Drag-to-reschedule on a **non-released** plan version (one attempt on a Released plan was correctly blocked/inconclusive by design; needs retesting against a Feasible plan).
- Right-click context menu on operation blocks ([GanttOperationContextMenu.razor](../src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationContextMenu.razor)).
- Write actions on Inventory, Rolling & Finishing, Steelmaking & Casting pages (read-only inspected only).
- Work Orders detail actions (Hold/execute/actuals entry).

## Suggested remediation order

1. Fix the `MTO-SO-1001-10` / `STD-BAR` route-grade seed mismatch (#1) — unblocks the entire "create a scenario" workflow for demo/dev use.
2. Fix `ToggleReconcileForm` wiring (#2) and the `bg-danger-soft0` typo (#3) — both are small, high-visibility fixes.
3. Add a confirmation step to Master Data deletes (#4).
4. Either link Control Tower into the Workspaces menu or remove it if superseded (#5).
5. Decide whether Capacity/Delivery need a real drill-through page or should stay summary-only by design (#6) — currently ambiguous, worth a product decision rather than a code fix.

## Implementation follow-up on `codex/ui-workbench-chrome-legibility`

The changes below were implemented after the live audit. **They are source-level remediation only until the APS Windows runtime gate is rerun; do not reinterpret this section as live verification.** The original findings above are deliberately preserved as the evidence baseline.

| Area | Implemented change | Commit | Runtime status |
|---|---|---|---|
| Master-data blocker (#1) | Added a targeted EF data migration that corrects the contradictory `STD-BAR` HotRoll transformation and matching route/generic capability rows from `BLT-150SQ→BLT-150SQ` to `BLT-150SQ→RND-12`; historical Plan Version snapshots are not mutated | `a686d9f` | Pending migration + Calculate rerun on Windows |
| Control Tower navigation (#5) | Added `/control-tower` to Workspaces menu | `3a59a91` | Pending Windows rerun |
| Capacity / Delivery drill-through (#6) | Capacity now opens Control Tower; Delivery opens Demand & Supply | `daea34e` | Pending Windows rerun |
| Demand reconciliation (#2) | Replaced the fragile toggle-only entry with a native disclosure form, validation, trimmed inputs, refresh-after-submit and responsive layout | `3bd966b`, `0c08f4c` | Pending Windows rerun |
| Demand shortfall bar (#3) | Corrected `bg-danger-soft0` to `bg-danger-soft` | `3bd966b` | Pending Windows rerun |
| Master Data delete guard (#4) | Added shared browser/desktop confirmation guard for delete actions on `/plan/master-data` | `1ae4d84`, `e2132e8`, `c3a44b8` | Pending Windows rerun |
| Plan lifecycle | Added explicit Calculate → Approve → Release flow, request validation, actionable master-data error path and released-work-order handoff | `3402030` | Pending Windows rerun |
| What-if analysis | Expanded persisted Plan Version comparison into aligned Baseline/What-if schedule visualizations, schedule footprint deltas, per-resource work-content deltas, assumption changes and exact operation-level consequences | `9338652`–`61230ef` | Pending Windows rerun |
| Capability calendar | Added a resource-focused week calendar combining route qualifications with time-phased downtime/derating, quick calendar entry, exact UTC interval editing and a central non-overlap invariant | `c48b909`–`7f6f5be` | Pending Windows rerun |
| Work Orders | Fixed selection loss after saving Work Order/operation actuals; added input guards, responsive master-detail layout and traceability handoff | `762aa72` | Pending Windows rerun |
| Steelmaking & Casting | Fixed selected-heat loss after saving heat actuals; added actual validation, responsive layout and execution/material handoffs | `699b7e5` | Pending Windows rerun |
| Rolling & Finishing | Made register/detail, downstream route, pegging table and packaging units responsive; added material/demand navigation | `afbdf19` | Pending Windows rerun |
| Inventory | Added inventory KPIs, projected-shortage emphasis, empty/filter states, responsive table and Material Flow handoff | `0d8f810` | Pending Windows rerun |
| Plan Compare | Fixed potential stuck `Comparing…` state on exceptions, prevented same-version compares, added errors/empty states and responsive comparison UI | `30afb58` | Pending Windows rerun |
| Control Tower UX | Added workbench/version/execution handoffs, responsive panels/tables and clearer execution/resource navigation | `3257d16` | Pending Windows rerun |

### Newly found during remediation

Source review of the previously unverified execution actions found two concrete state-restoration defects:

1. **Work Orders actual save could jump to the first Work Order.** The old save flow called `LoadAsync()`, which replaced `selectedWorkOrder`, then attempted to restore selection using the already-replaced object. The updated flow captures the Work Order and operation identifiers before save and reloads them explicitly (`762aa72`).
2. **Steelmaking heat actual save had the same pattern.** `LoadAsync()` replaced `selectedHeat` before the old code attempted to recover its ID. The updated flow captures `CampaignHeatId` before save and restores that heat after refresh (`699b7e5`).

These two findings were identified statically and are **not yet live-verified**. They should be added to the next runtime interaction pass alongside the original not-yet-verified actions.

### Original blocker status

Finding #1 now has a source-controlled data repair instead of a UI workaround. Migration `20260904190000_RepairStdBarMasterData` updates only the known contradictory current master rows for `STD-BAR` HotRoll and intentionally leaves immutable historical Plan Version snapshots alone. Both desktop and service hosts already run EF migrations at startup, so the persisted local SQLite master data will be corrected when the updated build starts. **The blocker is not marked live-fixed until that migration is applied on the Windows runtime and Calculate is rerun successfully.**
