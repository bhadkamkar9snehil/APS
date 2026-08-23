# APS Gantt Workbench — Pre-Implementation Reconnaissance

Status: reconnaissance only. No application code was changed to produce this document. It exists so
that a follow-up implementation pass (referred to below as "Codex") can spend its context budget on
building the overhaul rather than rediscovering the current system.

Authoritative specs for the overhaul itself remain:
- `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md` — the binding requirement set (KEEP/FIX/LYT/DND/DEP/CAP/CAL/CMP/APS/CTX/HIS/EXE/A11Y/PERF/UTL, Sections 21-29).
- `docs/reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md` — the behavioral reference bar.

This document does not restate either in full. It records what independent code archaeology and a real
runtime pass against the existing local database confirmed, disproved, or added to those two documents,
and turns that into an implementation-ready decomposition.

---

## 1. Scope and method

- Repository: `bhadkamkar9snehil/APS`, branch `claude/project-status-review-o2dx1j`.
- Commit inspected: `2f159a4ba1ce3b7d9b64d12b74df28e4515b7105` (HEAD at the time of this pass; fast-forwarded
  from `8f672c3` via `git pull --ff-only` because the branch was two commits behind origin and there was
  nothing local to lose — see git safety note below).
- Nature of the pass: read-only code audit + a real build/test/run of the app against the pre-existing
  local SQLite database at `/root/.local/share/APS-Data/Data/aps.db`. No schema change, no reseed, no
  migration was run. The app was stopped (`pkill`) after screenshot capture; the `.db`/`.db-wal`/`.db-shm`
  files were not touched by this session beyond normal read/write traffic from running the service the
  user already had provisioned.
- Git safety: `git status --short --branch` was clean (no local changes) before the fast-forward pull, so
  the pull was non-destructive. No `reset --hard`, `clean`, `checkout --`, or force-push was used anywhere
  in this pass.
- No Computer Use was used. All runtime interaction was via a scripted Playwright session (Node, using
  the pre-installed Chromium at `/opt/pw-browsers`) driving the real Blazor Server host, `APS.Service`.

---

## 2. Authoritative documents read in full

| Document | One-line takeaway |
| --- | --- |
| `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md` | Binding target spec: KEEP-01..08, FIX-01..13, LYT/DND/DEP/CAP/CAL/CMP/APS/CTX/HIS/EXE/A11Y/PERF/UTL requirement IDs, DHTMLX disposition table, component architecture (ARC-001..006), DTO/CMD gaps, acceptance tests, phased sequence. |
| `docs/reference/APS_GANTT_DHTMLX_BEHAVIORAL_BENCHMARK.md` | Deep explanation of *how* DHTMLX actually behaves (not just feature names), plus an explicit reject list and a draft behavioral scorecard against the current branch. |
| `docs/superpowers/specs/2026-08-21-planning-workbench-design.md` | Governing "Unified Planner Cockpit" spec: domain lifecycle, desktop menu contents, 7-region screen anatomy, exact sizing budgets, workbench lenses. |
| `docs/superpowers/plans/2026-08-21-planning-workbench.md` | The original 8-task plan that produced the *current* workbench contracts/services/state. Its Task 4/5 called for Gantt component extraction (`WorkbenchGantt.razor`, `GanttTimeline.razor`, `GanttResourceLane.razor`, `GanttOperationBlock.razor`, `GanttCampaignSpan.razor`, `GanttDependencyLayer.razor`) that was **never carried out** — confirmed by directory listing: none of these files exist under `src/APS.UI/Components/`. |
| `docs/superpowers/plans/2026-08-21-unified-planner-cockpit.md` | Produced `DesktopMenuBar.razor`/`DesktopMenu.razor`, `PlannerCockpitState.cs`, and the `--aps-visible-lanes` adaptive-height CSS — all present and in active use today. |
| Root `README.md` | Canonical architecture: manufacturing routes are not a fixed topology, campaign optimization is not sort-and-fill, canonical production lifecycle chain, and an explicit rule that CI must never be cited as verification — only local runs count. |
| `docs/current/README.md` | Documentation-authority index: `docs/current/` is canonical, `docs/reference/` is non-authoritative, `docs/archive/` is superseded. This document is being placed in `docs/current/` per that convention. |

---

## 3. Current Gantt implementation map

All files below were read in full (not sampled).

| File | Lines | Role |
| --- | ---: | --- |
| `src/APS.UI/Components/Pages/FiniteSchedule.razor` | 579 | The entire workbench page: scenario header wiring, toolbar, Gantt grid/lanes/bars, dependency SVG, drag JS-interop entry points, queue/inspector panels, all in one file. |
| `src/APS.UI/wwwroot/planning-workbench.js` | 144 | All pointer/drag/autoscroll/snap mechanics for the Gantt. |
| `src/APS.UI/State/PlanningWorkbenchState.cs` | 257 | Zoom/pan/selection/toggle state, plus a second (dead) undo/redo stack and dead `QueueOpen`/`InspectorOpen` flags. |
| `src/APS.UI/State/PlannerCockpitState.cs` | 54 | The state actually driving queue/inspector/analysis-dock visibility (`OpenDrawer` single-value enum) and menu command routing. |
| `src/APS.Application/PlanningWorkbenchContracts.cs` | 73 | Read-model DTOs: `PlanningWorkbenchView`, `ScheduleResourceLaneView`, `ScheduledProcessOperationView`, `PlanningOperationDetailView`, `PlanningOperationResourceOptionView`, etc. |
| `src/APS.Application/PlanningWorkbenchCommandContracts.cs` | 57 | Command DTOs: `PlanningMoveProposal`, `PlanningProposalImpact`, `PlanningConstraintFinding`, `PlanningMoveApplyRequest/Result`. |
| `src/APS.Infrastructure/PlannerWorkspaceQueryService.Workbench.cs` | 223 | `GetPlanningWorkbenchAsync` aggregation — batched EF queries, in-memory joins, no N+1. Not the perf bottleneck. |
| `src/APS.Infrastructure/PlanningWorkbenchCommandService.cs` | 236 | `ValidateMoveAsync`/`ApplyMoveAsync` — a genuinely rich constraint engine (see §10). |
| `src/APS.UI/Components/PlanningWorkbench/*.razor` (5 files) | 47–60 each | `WorkbenchScenarioHeader`, `WorkbenchLifecycleRail`, `WorkbenchAnalysisDock`, `AnalysisMessage`, `SummaryCard` — small, clean, already-extracted chrome components. |
| `src/APS.UI/Components/Layout/{MainLayout,DesktopMenuBar,PlanContextBar}.razor` | 112/63/59 | Application chrome. `PlanContextBar.razor` is **dead** — grep confirms zero references anywhere in `src/`. |
| `src/APS.UI/wwwroot/tailwind-input.css` | 464 | Design tokens, elevation ladder, `.aps-operation`/`.aps-op-card`/`.aps-lane-drop-target`/`.aps-snap-guide` drag-mechanic classes, `.aps-gantt-lanes` adaptive row-height grid. |
| `tests/APS.Planning.Tests/PlanningWorkbenchCommandTests.cs` | 156 | 2 tests: eligible move applies cleanly; resource overlap produces a warning, not a block. |
| `tests/APS.Planning.Tests/PlanningWorkbenchQueryTests.cs` | 149 | 1 aggregation test covering schedule/demand/campaign/material/exceptions for one plan. |
| `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs` | 86 | 6 string-matching tests against the razor source itself — asserts labels exist, `Enum.GetValues` patterns are absent, dependency layer is chain-focused. |
| `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs` | 139 | 8 tests against `PlanningWorkbenchState` in isolation — including `UndoPlan`/`RedoPlan`/`InspectorOpen`, which pass at the state level even though the page never wires them to rendering (see §5, FIX-12). |

### 3.1 The full move/replan trace (confirmed end-to-end)

`FiniteSchedule.razor` → operation card `@onclick="() => SelectOperation(operation)"` (`FiniteSchedule.razor:205`)
→ inspector "Preview impact" calls `Commands.ValidateMoveAsync(PlanningMoveProposal)`
→ `PlanningWorkbenchCommandService.ValidateMoveAsync` (`PlanningWorkbenchCommandService.cs:12-184`) runs eight
independent constraint checks against live EF-Core state (not cached/stale data): resource eligibility via
`PlanOperationResourceOptionSnapshots`, operating-state availability, horizon bounds, **frozen time fence**
(`PlanningTimeFencePolicy().FrozenMinutes`), resource calendar conflicts, disjunctive-resource overlap
(splitting into a hard block if the overlapping op is itself frozen vs. a solver-repair warning if not),
predecessor/successor repair-need warnings
→ result becomes `PlanningProposalImpact` (`CanApply` + typed `PlanningConstraintFinding[]`)
→ "Apply move" calls `Commands.ApplyMoveAsync(PlanningMoveApplyRequest)` (`PlanningWorkbenchCommandService.cs:186-220`)
→ re-validates, builds an `OperationScheduleOverride`, calls `lifecycle.ReplanAsync(baselinePlanVersionId, PlanningRecalculationRequest{...ScheduleOverrides, RepairScope...})`
→ this persists a new Plan Version and the page reloads it via `LoadAsync`.

This is a real, non-trivial constraint pipeline already — richer than what the Gantt UI currently surfaces
(see §10, "reusable backend assets"). The staged-drag path (`StageDraggedMove` JSInvokable,
`FiniteSchedule.razor:446-455`) is a *separate*, thinner path: it receives a raw 0-1 ratio from JS across the
entire visible window and re-rounds it to 15 minutes in C# — duplicating the 15-minute constant that also
lives in `planning-workbench.js:3` (two independent hardcodes of the same snap policy, confirmed FIX-04).

---

## 4. Defect confirmation (FIX-01 through FIX-13)

Every FIX item in the requirements doc was independently re-verified against source and, where practical,
against the live running app (screenshots referenced by filename; all under the reconnaissance screenshot
set captured this session — see §6). None were disproved. Two were only source-confirmed (FIX-11, FIX-13)
because they don't have a distinctive visual signature in the seeded demo data used for this pass.

| ID | Claim | Verdict | Evidence |
| --- | --- | --- | --- |
| FIX-01 | Drag snaps pointer x-coordinate to the grid, not grab-offset-preserving | **CONFIRMED** | `planning-workbench.js:60-66` (`down` handler) + `:18-30` (`snap()`) never capture `pointerTimeAtDown - operationStart`; `up` handler (`:123`) uses `snapped.ratio` from raw `clientX`. |
| FIX-02 | Drag moves the real DOM node, no ghost | **CONFIRMED, live** | `planning-workbench.js:76` mutates `state.drag.block.style.transform` where `state.drag.block` (`:62`) is `event.target.closest('.aps-operation')` — the actual card. Screenshot `10-drag-in-progress.png` shows the source card itself lifted with a shadow at its original grid position; no second/ghost element exists. |
| FIX-03 | Any lane under pointer gets drop-target highlight, no eligibility check | **CONFIRMED, live** | `planning-workbench.js:80-87` applies `.aps-lane-drop-target` to any `[data-resource-id]` under the pointer. Screenshot `11-drag-cross-lane.png` shows an EAF operation being dragged over the **CCM-1** lane — a resource type it cannot legally run on — and CCM-1 lights up as a valid target exactly like an eligible EAF lane would. The subsequent drop (`12-post-drop-impact-or-proposal.png`) silently snapped back to origin with **no error/toast shown at all** — an additional finding beyond the doc's FIX-03 text: invalid drops fail silently, with zero user feedback. |
| FIX-04 | Fixed 15-minute snap, no policy | **CONFIRMED** | `planning-workbench.js:3`: `const SNAP_MINUTES = 15;`. Duplicated server-side at `FiniteSchedule.razor:446-455` (`Math.Round(minutes / 15d) * 15d`) — two independent hardcodes of one policy value. |
| FIX-05 | Autoscroll is horizontal-only | **CONFIRMED** | `planning-workbench.js:48-58` (`autoScroll`) only reads `clientX`/`rect.left`/`rect.right` and only mutates `scrollLeft`. No vertical branch exists at all. |
| FIX-06 | "Tight chain" is a ±1-minute adjacency test dressed up as critical path | **CONFIRMED** | `FiniteSchedule.razor:507`: `IsTightChain` = `Math.Abs((op.StartUtc - x.EndUtc).TotalMinutes) <= 1d`. No slack, no solver input, no constraint category. |
| FIX-07 | Fixed 176px resource column, no real grid/timeline split | **CONFIRMED** | `grid-cols-[176px_1fr]` hardcoded twice, `FiniteSchedule.razor:157` and `:184`. No resizer, no persisted width, no min/max. |
| FIX-08 | Single-tier time axis, coarse step buckets | **CONFIRMED** | `AxisTicks()` (`FiniteSchedule.razor:508`) — one tier, step rule `hours<=12?1h:hours<=36?4h:hours<=96?12h:1d`, one format string. Screenshot `03-zoom-7-d.png` shows only day labels with no hour sub-ticks at any zoom. |
| FIX-09 | Straight, unrouted dependency lines | **CONFIRMED, live** | `FocusedDependencyLines()` (`FiniteSchedule.razor:495-506`) emits raw SVG `<line>` from lane-center X/Y percentages via a BFS over predecessor keys — no ports, no orthogonal routing, no arrowheads, no lag/type. Screenshot `08-dependencies-enabled.png` shows a thin diagonal dashed line cutting straight through unrelated operation cards on its way from CCM-1 to RHF-1. |
| FIX-10 | Operation cards overloaded with 6+ data points in ~10px text | **CONFIRMED, live** | `FiniteSchedule.razor:204-229` button markup; visible in every schedule screenshot as `PO-FLEX-3 / 00:00… 60 MT / G-F… Heat 03` crushed into a card that is unreadable below roughly 1-day zoom, and at 7-day zoom (`03-zoom-7-d.png`) degrades to unlabeled colored slivers with no content-adapts-to-width logic and no aggregation of same-resource adjacent slivers. |
| FIX-11 | Every distinct due timestamp becomes its own full-height marker line, no density control | **CONFIRMED (source only)** | `DueMarkers()` (`FiniteSchedule.razor:509`) groups strictly by exact `RequiredDate` with no zoom-aware bucketing and no exceptional/selected-only visual weighting; every group renders a full-height `bg-warning` line (`:164`). Not visually distinctive in this session's screenshots because the seeded demo data's due dates were sparse across the visible window — the code path is unconditionally present, so the "forest" appears once due-date density is realistic (which is the normal case for a production book of orders). |
| FIX-12 | Duplicate/overlapping state ownership for drawers and undo/redo | **CONFIRMED, more precisely than the doc states** | Two independent, unsynchronized duplications exist, not one: (a) **Undo/redo** — `PlanningWorkbenchState` has a fully-implemented, unit-tested `Stack<PlanHistoryEntry>` (`PlanningWorkbenchState.cs:39-40,135-157`, exercised by `PlanningWorkbenchStateTests.cs:101-112`) that is **never called from the page** (confirmed via repo-wide grep for `UndoPlan()`/`RedoPlan()`); the actual Undo/Redo buttons in `WorkbenchScenarioHeader.razor:19-20` are wired to a second, page-private `Stack<(Guid,Guid)>` (`FiniteSchedule.razor:365-366,372-373,461,468,473-474`). Worse, `ApplyMoveAsync` records history (`:461`) but `ReplanAsync`/Optimize does **not** (`:468`) — an optimize-triggered replan silently doesn't become undoable, while a drag-move does. (b) **Drawer visibility** — `PlanningWorkbenchState.InspectorOpen`/`QueueOpen` toggle correctly and are unit-tested (`PlanningWorkbenchStateTests.cs:114-124`), but the actual UI gates every drawer render on `PlannerCockpitState.InspectorOpen`/`QueueOpen`/`OpenDrawer` instead (`FiniteSchedule.razor:83-84,94,245`). Confirmed live: `SelectOperation()` calls `state.SelectOperation(...)` (which flips the *tested-but-unused* `state.InspectorOpen`) but never calls `Cockpit.ToggleInspector()`, so selecting an operation in the running app does **not** open the real inspector panel — screenshot `04-operation-selected.png` (card selected, no drawer) vs. `05-inspector-open.png` (drawer only appears after an *explicit* separate click on the "Inspector" button). |
| FIX-13 | Real Gantt still lives in one monolithic page despite an existing plan calling for extraction | **CONFIRMED** | `FiniteSchedule.razor` is 579 lines and owns scenario header wiring, the full toolbar, grid/lane/bar markup, SVG dependency layer, and all JS-interop entry points. The 2026-08-21 planning-workbench plan's Task 4/5 (`WorkbenchGantt.razor`, `GanttTimeline.razor`, `GanttResourceLane.razor`, `GanttOperationBlock.razor`, `GanttCampaignSpan.razor`, `GanttDependencyLayer.razor`) were never created — confirmed absent via `Glob` against `src/APS.UI/Components/`. The five components that *do* exist under `PlanningWorkbench/` (§3 table) are all chrome (header/rail/dock), not Gantt rendering. |

### 4.1 Additional findings beyond the named 13

1. **Silent invalid-drop failure** (extends FIX-03): dropping on an ineligible lane produces no error message,
   no shake/reject animation, nothing — the card just reverts. A planner has no way to learn *why* a drop
   didn't take.
2. **Rendering complexity / perf risk** (relevant to PERF-001/PERF-004/PERF-005): `VisibleLanes`
   (`FiniteSchedule.razor:161`, referenced 9× in the file) is an **uncached C# property** that recomputes
   `BuildVisibleLanes()` — a `Where(...Any(...))` filter followed by `OrderBy().ThenBy()` — on every single
   access, not once per render. `FindDetail(` (10 call sites) and `FindOperation(` (6 call sites) are both
   O(N) unmemoized linear scans over the full operation list, invoked repeatedly per visible operation per
   render (directly at the card level, again inside `IsTightChain` when tight-chain mode is active, and
   again inside label/title formatting). With `VisibleOperations()` also being recomputed per lane rather
   than once, total per-render cost scales roughly as **O(visibleOperationCount × totalOperationCount)**
   with a nontrivial constant from the repeated `VisibleLanes` LINQ pipeline. At the 30-operation, 5-lane
   demo scale this is invisible; the requirements doc's own acceptance test 25.5.1 targets a 10,000-operation
   scenario, at which point this access pattern is the dominant risk to PERF-004's 60fps/no-per-frame-recompute
   target and directly motivates PERF-001 (row virtualization) as a Phase-0, not Phase-1, item.
3. **`PlanContextBar.razor` is dead code** — a fully-built plan-context chrome component (version, status,
   horizon, reference time, solver status, trigger — `PlanContextBar.razor:1-45`) with zero references
   anywhere in `src/`. It is not rendered by `MainLayout.razor`, which renders only `<DesktopMenuBar />`
   plus `@Body`. Worth a decision in the overhaul: delete it, or revive it as the compact plan-context strip
   the design docs describe — its content already matches that need almost exactly.
4. **The backend constraint model is already ahead of the UI** — see §10.

---

## 5. Screenshot evidence

Captured against the real `APS.Service` host (Kestrel, `http://localhost:5187`) running against the
pre-existing local database (scenario `20260821-161630-DC3449`, Feasible, 30 operations / 5 resources,
15 Sep 2026 00:00 → 15:15 schedule window), at 1920×1080 viewport. All 17 files remain in the session
scratchpad (`/tmp/.../scratchpad/shots/`); they are evidence artifacts for this reconnaissance pass, not
committed to the repo.

| File | Shows |
| --- | --- |
| `01-initial-workbench.png` | Full chrome stack + 5-lane Gantt at default Fit zoom. Confirms exact chrome order (see §7). |
| `02-resource-lanes-context.png` | Same, lane column detail. |
| `03-zoom-8-h.png` / `-1-d.png` / `-3-d.png` / `-7-d.png` / `-fit.png` | All 5 named `PlanningWorkbenchZoom` levels. At `7-d`, operations degrade to unlabeled colored slivers with no adjacent-op aggregation (FIX-10 live evidence). |
| `04-operation-selected.png` | Operation selected; inspector does **not** auto-open (FIX-12 live evidence). |
| `05-inspector-open.png` | Planner Inspector opened explicitly — PLAN/ACTUALS/LINEAGE sections, "Move or reassign" form with eligible-resource dropdown, target start, reason, planner note, "Authorized disruption override" checkbox, Preview impact/Apply move buttons. This is a materially richer inspector than the Gantt canvas itself currently exposes — a reusable asset (§10). |
| `06-queue-open.png` | Demand queue drawer — order cards with due date/priority/status, "Clear focus" affordance. |
| `07-baseline-enabled.png` | Baseline toggle on — no visible overlay/compare change in this dataset (no divergent baseline persisted), consistent with KEEP-06/CMP requirements being unimplemented, not merely off. |
| `08-dependencies-enabled.png` | "Selected chain" toggle — confirms FIX-09 straight-line rendering live. |
| `09-tight-chain-enabled.png` | "Tight chain" toggle — ring highlight on adjacent-in-time operations. |
| `10-drag-in-progress.png` | Mid-drag on the EAF-1 lane — confirms FIX-02 (real card moved, no ghost) and shows the whole lane highlighted rather than a precise drop-zone affordance. |
| `11-drag-cross-lane.png` | Same drag continued onto the CCM-1 lane — confirms FIX-03 live (an EAF operation is shown as droppable onto a CCM resource). |
| `12-post-drop-impact-or-proposal.png` | After releasing on the ineligible lane — card silently reverted to origin, no error UI (additional finding, §4.1.1). |
| `13-execution-mode.png` | Lifecycle rail "Execution" tab — operation cards gain "Planned" execution-status suffixes; otherwise structurally identical to Plan mode (EXE-* requirements largely unimplemented). |
| `14-analysis-dock-open.png` | Bottom analysis dock, "Overview" view — 4 summary cards (Plan health, Delivery exposure, Planning focus, Next decision). |

---

## 6. DHTMLX mechanics cross-reference

The requirements doc's Section 21 table (DHTMLX capability → APS disposition) and the benchmark doc's
Section 15 reject-list were treated as hypotheses and checked against what the current code actually does,
not copied blind. No corrections to that table were needed — the code audit and screenshots are consistent
with every disposition in it. The condensed, code-grounded version below groups by what changes for Codex:

**COPY MECHANIC (build the DHTMLX behavior close to as-is because nothing APS-specific changes it):**
- Grab-offset-preserving drag anchor (FIX-01 fix).
- Ghost/ghost-follows-pointer with fixed source (FIX-02 fix).
- 2D proportional-edge autoscroll with ramped speed (FIX-05 fix).
- Pointer-anchored zoom/zoom-to-fit/reset-zoom.
- Smart/virtualized rendering of only visible rows and time cells (directly motivated by the perf finding in §4.1.2).
- Synchronized grid/timeline split with a real resizer, persisted width (FIX-07 fix).
- Dual-tier time scale (FIX-08 fix).
- Orthogonal routed dependency paths with ports/arrowheads (FIX-09 fix, but see ADAPT below for what triggers a line existing at all).

**ADAPT TO APS (same shape, APS-specific meaning):**
- Critical path → **Binding chain**: DHTMLX's slack-based critical path becomes a solver-derived binding
  flag + constraint category (predecessor/resource-sequence/campaign-sequence/material/thermal/fence/due-date),
  not a temporal-adjacency heuristic (FIX-06 fix). This is a **BACKEND PREREQUISITE** item — see DTO-002 below;
  the UI cannot legitimately show this until the solver/read-model exposes slack per operation.
- Resource assignment/eligible-alternatives → the inspector's `PlanningOperationResourceOptionView`
  (`PlanningWorkbenchContracts.cs`) already carries `ResourceId, ResourceCode, ResourceName, DurationMinutes,
  AssignmentPenalty, WasSelected, EligibilityBasisCode` — a reusable asset, currently only surfaced in the
  inspector's `<select>`, that should drive Gantt drag-eligibility highlighting directly (fixes FIX-03 for real,
  not just cosmetically).
- Baselines (3 density modes) → APS's baseline is a persisted, immutable Released Plan Version, so "baseline"
  in APS terms is a full comparison mode (ghost overlay / compare subrow / changed-only), not a generic
  same-project revision.
- Resource histogram → APS capacity buckets are process-specific (processing/setup/changeover/downtime/idle/
  overload), which DHTMLX does not compute itself either — it also expects the host app to supply load data.
- Working calendars → real `ResourceCalendars` already exist and are already checked in
  `ValidateMoveAsync` (`PlanningWorkbenchCommandService.cs:82-94`) but are **not rendered** on the Gantt at all
  today; this is a pure UI gap, not a backend gap.

**DO NOT COPY (explicitly rejected, confirmed nothing in the current or planned APS domain model wants this):**
- Inline grid cell editing of resource master data.
- Placeholder "new task" row (operations are solver-derived, never hand-created on the Gantt).
- Progress-drag (actual production output owns progress, not a UI knob — confirmed no `Progress` write path
  exists anywhere in `PlanningWorkbenchCommandContracts.cs`).
- Drag-to-create dependency links (route dependencies come from the configured `ManufacturingRoute`, never ad hoc).
- Lightbox modal task editor (the modeless inspector, already built and screenshotted in `05-inspector-open.png`,
  is strictly better and already the KEEP-05 direction).
- Generic "unassigned" bucket equated with the demand queue (`06-queue-open.png` confirms the demand queue is
  already its own domain object with due date/priority/status — a different semantic than DHTMLX's unscheduled-task grouping).

**BACKEND PREREQUISITE (the UI mechanic cannot be honestly built until the read-model or solver changes ship):**
- Binding chain / slack explanation (DTO-002) — no slack field exists anywhere in
  `PlanningOperationDetailView` or `ScheduledProcessOperationView` today.
- Rich dependency links with type/lag (DTO-001) — `PredecessorPlanningKeys` is a bare `IReadOnlyCollection<string>`,
  confirmed in `PlanningWorkbenchContracts.cs`.
- Occupancy segments / setup-changeover sub-intervals (DTO-003) — no such field on `ScheduledProcessOperationView`.
- Resource hierarchy for grid grouping (DTO-005) — `ScheduleResourceLaneView` has no parent/area/plant field.
- Capacity buckets (DTO-007) — nothing computes aggregated load today; `PlannerWorkspaceQueryService.Workbench.cs`
  aggregates schedule/demand/campaign/material/exceptions but not resource utilization buckets.

---

## 7. Information density / chrome-height budget

Measured directly from `01-initial-workbench.png` (1920×1080 viewport) and cross-checked against the Tailwind
classes that produce each band:

| Band | Source | Approx. height |
| --- | --- | ---: |
| Application menu bar | `DesktopMenuBar.razor:6` (`h-8` + border) | 33px |
| Scenario header | `WorkbenchScenarioHeader.razor:3-4` (`py-1.5`, `min-h-9`) | ~41px |
| Lifecycle rail row | `FiniteSchedule.razor:57-62` (`border-b`, rail buttons `py-1.5`) | ~34px |
| Toolbar (zoom/search/toggles) | `FiniteSchedule.razor:64-86` (`px-4 py-2`, buttons `py-1`) | ~52px |
| "Visible … Mode … shown" info row + Gantt column header | not yet extracted into its own component | ~53px combined |
| **Total chrome above the canvas** | | **~213px (≈19.7% of 1080px)** |
| Bottom analysis dock, collapsed | `WorkbenchAnalysisDock.razor:2` (`h-8`) | 32px |
| Bottom analysis dock, expanded | `WorkbenchAnalysisDock.razor:14` (`h-[30vh] min-h-40`) | ~324px at 1080p |

This is already close to LYT-002's "~96-112px" budget for *persistent* chrome if the toolbar/info-row/lifecycle-rail
were consolidated or partially moved into menus — but as currently laid out it is roughly double that, because
four separate bordered rows (scenario header, lifecycle rail, toolbar, info row) are stacked instead of merged.
`PlanContextBar.razor` (dead, §4.1.3) is not part of this stack today, so reviving it would add, not remove,
vertical budget unless it replaces one of the existing rows.

**Per-control disposition** (against LYT-002's "collapse into menus" instruction):

| Control | Current location | Recommended disposition |
| --- | --- | --- |
| Earlier/Later pan buttons | Toolbar row | Keep permanent — primary navigation, used constantly. |
| 8h/1d/3d/7d/Fit zoom buttons | Toolbar row | Keep permanent, but compact into a single segmented control. |
| Search box | Toolbar row | Keep permanent. |
| Baseline / Selected chain / Tight chain toggles | Toolbar row | Keep permanent (they're high-frequency view state), but visually group as one toggle cluster. |
| Demand queue / Inspector open buttons | Toolbar row (duplicated in the View menu, `DesktopMenuBar.razor:22-23`) | Already dual-exposed (menu + toolbar) — correct pattern, no change needed. |
| "Objective profile: Delivery reliability" | Lifecycle rail row, right-aligned | Move to inspector/overview dock — it's informational, not actionable, and does not need permanent canvas real estate. |
| "Visible … Mode … shown" info line | Own row | Merge into the toolbar row (right-aligned) rather than a fifth stacked band. |
| Undo/Redo/Optimize/Validate/Release | Scenario header | Keep permanent — primary commands. |
| Create recovery/planning scenario buttons | Lifecycle rail row (contextual) | Keep contextual — correct pattern already. |

---

## 8. APS-specific scheduling semantics (design notes for Codex)

These are recommendations grounded in what the backend already models (§10) plus the domain rules in root
`README.md`, not a restatement of the requirements doc's own domain sections.

- **Resource hierarchy grouping**: `Resource` already carries `PlantId`/`ProcessStageId`
  (`PlanningWorkbenchCommandTests.cs:114-125` shows the shape used in tests). DTO-005's "parent/area/plant"
  read-model gap is therefore a projection gap, not a missing domain concept — the Gantt's resource grid
  should group by `ProcessStageId` first (matches the existing lane coloring by `ProcessOperationType`, see
  the color legend in every screenshot: EAF/LRF/VD/Cast/Reheat/Hot roll/Cold roll/Finish) and by `PlantId`
  above that only when a scenario spans multiple plants.
- **Alternate-resource UI treatment**: the inspector's `ELIGIBLE RESOURCE` dropdown (`05-inspector-open.png`)
  already lists `WasSelected`/`EligibilityBasisCode` per option. The Gantt drag layer should consume the same
  `PlanningOperationResourceOptionView` collection to compute FIX-03's eligible/ineligible lane set at drag-start,
  rather than inventing a second eligibility computation.
- **Binding chain, not critical path**: until DTO-002 ships, the Gantt must not claim to show a critical path.
  The honest interim state is what "Selected chain" already does — show the focused predecessor/successor
  chain for the selected operation with **no styling implying slack-derived criticality**. "Tight chain" should
  be removed or explicitly relabeled "adjacent-in-time" until real slack exists; keeping it as-is under any
  binding-chain-sounding name would be a regression relative to being upfront about the gap.
- **Campaign/heat overlay**: `PlanCampaignSnapshot`/`PlanHeatSnapshot` already exist and are exercised in
  `PlanningWorkbenchQueryTests.cs:71-100`; a campaign span layer over the Gantt (grouping the 6 `PO-FLEX-*`
  heats visible in every screenshot into one `CMP-00001` band) is a pure rendering addition, not a backend gap.
- **Material availability**: `result.Material.Pools` exists in the read model (asserted empty in
  `PlanningWorkbenchQueryTests.cs:144`) but nothing in `FiniteSchedule.razor` renders it on the Gantt today —
  it only reaches the analysis dock's "Material" tab (`WorkbenchAnalysisDock.razor:34`) as a static link-out.
- **Capacity/resource-load visualization**: no aggregation exists yet (DTO-007, backend prerequisite). The
  lane color legend already present (EAF/LRF/VD/Cast/Reheat/Hot roll/Cold roll/Finish, top-right of every
  screenshot) is a reusable visual language for a future histogram.
- **Planning-fence visual semantics**: a "Frozen" fence **is already rendered** — a shaded pink region plus a
  red "Frozen" label at the left edge of the timeline in every screenshot, and it is backed by real logic
  (`FrozenMinutes` check in `ValidateMoveAsync`). This is a KEEP item, not a gap; the requirements doc's
  CAL-* section should be read as "extend this pattern to firm-stable/flexible bands," not "build fence
  rendering from zero."
- **Baseline/comparison system**: `state.ShowBaseline` exists and toggles (`07-baseline-enabled.png`) but
  produces no visible change against this dataset — consistent with no baseline-diff data path existing yet.
  This is squarely CMP-*/DTO-006 (backend prerequisite for the "full baseline placement" mode; the doc's
  "ghost overlay/compare subrow/changed-only" three-mode design is a reasonable target once that data exists).

---

## 9. Reusable backend assets (do not rebuild these)

The single biggest risk for an implementation pass is spending time re-building constraint logic that already
exists and is already tested. Confirmed present and correct:

1. **`PlanningWorkbenchCommandService.ValidateMoveAsync`** — resource eligibility, operating-state availability,
   horizon bounds, frozen-fence check, resource-calendar conflict, disjunctive-resource overlap (frozen vs.
   solver-repairable), predecessor/successor repair-need warnings. All of FIX-03's "real eligibility" and
   CAL-*'s "frozen fence" requirements are backend-solved already; the work is wiring the Gantt's drag layer
   to call/reflect this, not writing new validation.
2. **`PlanningOperationResourceOptionView`** — already has everything DND needs for eligible-lane-before-drop.
3. **The proposal→validate→impact→apply→replan pipeline** — already matches the requirements doc's KEEP-05
   staged-proposal model exactly; do not redesign this flow, only redesign its Gantt-side presentation (ghost
   instead of moved-real-bar, per-lane eligibility affordance instead of post-hoc reversion).
4. **Frozen-fence rendering** — already exists visually and is backend-correct; extend, don't rebuild.
5. **Campaign/heat/demand read-model shapes** — already queried and tested; only the Gantt-layer rendering is missing.

---

## 10. Backend/read-model gaps that actually block target behavior

Re-confirmed against the real contracts (not assumed from the requirements doc's text):

| Gap | Confirmed absent from | Blocks |
| --- | --- | --- |
| DTO-001 dependency links (type/lag) | `PredecessorPlanningKeys` is `IReadOnlyCollection<string>` in `PlanningWorkbenchContracts.cs` | Routed dependency rendering with real semantics |
| DTO-002 slack/binding explanation | No such field on `PlanningOperationDetailView`/`ScheduledProcessOperationView` | Binding-chain concept (replacement for "tight chain") |
| DTO-003 occupancy segments | No setup/changeover field on `ScheduledProcessOperationView` | Sub-bar segment rendering |
| DTO-004 resource calendar intervals in the visible window | Calendars are checked server-side but never returned to the read model for rendering | Visualizing resource downtime/unavailability on the Gantt itself |
| DTO-005 resource hierarchy | No parent/area field on `ScheduleResourceLaneView` (though `Resource.PlantId`/`ProcessStageId` exist at the entity level) | Grouped/collapsible resource grid |
| DTO-006 full baseline placement | Nothing in the read model returns a full alternate-plan placement set | Ghost-overlay/compare-subrow baseline modes |
| DTO-007 capacity buckets | No aggregation exists in `PlannerWorkspaceQueryService.Workbench.cs` | Resource histogram/capacity panel |

None of these are large lifts individually (all are read-model projection additions over data that mostly
already exists in the domain model or is one join away), but every one of them gates a Phase 0/1 UI feature,
so they should be scheduled *before* the UI work that depends on them, not discovered mid-slice.

---

## 11. Target component architecture (mapped onto current files)

The requirements doc's ARC-001 component list (`WorkbenchGantt.razor`, `GanttResourceGrid.razor`,
`GanttTimeScale.razor`, `GanttTimelineViewport.razor`, `GanttResourceLane.razor`, `GanttOperationLayer.razor`,
`GanttBaselineLayer.razor`, `GanttDependencyLayer.razor`, `GanttMarkerLayer.razor`, `GanttCalendarLayer.razor`,
`GanttCampaignLayer.razor`, `GanttProposalLayer.razor`, `GanttCapacityPanel.razor`, `GanttTooltip.razor`) does
not exist yet in any form. The extraction target is entirely inside the current
`FiniteSchedule.razor:157-360`-ish region (grid/lane/bar/SVG markup) plus `planning-workbench.js` in full.
The five existing `PlanningWorkbench/*.razor` components (chrome only) are unaffected by this decomposition
and should not be touched by it. `PlanningWorkbenchState` and `PlannerCockpitState` both need to be resolved
into ARC-002/003/004's "one viewport/selection/proposal state" before or during Phase 0, since FIX-12's
duplicate-state problem is exactly what ARC-002/003/004 exist to fix.

---

## 12. Codex implementation-slice decomposition

Ordered so each slice is independently buildable, testable, and runtime-verifiable, and so backend
prerequisites land before the UI slices that need them. Slice numbering is this document's own, not the
requirements doc's phase numbering (referenced in parentheses where it aligns).

### Slice 0 — State consolidation (Phase 0 item 9; fixes FIX-12)
- **Goal**: one canonical workbench state graph.
- **Existing files affected**: `PlanningWorkbenchState.cs` (delete dead undo/redo stack, dead `QueueOpen`/`InspectorOpen`), `PlannerCockpitState.cs` (becomes the sole drawer/history owner or is merged), `FiniteSchedule.razor` (rewire `SelectOperation` to actually open the inspector), `WorkbenchScenarioHeader.razor` (Undo/Redo wiring).
- **New components/state**: none required yet — this is a consolidation slice, not an extraction slice.
- **Backend changes**: none.
- **Tests**: update `PlanningWorkbenchStateTests.cs` to test the consolidated shape; delete assertions against removed dead members.
- **Runtime verification**: select an operation in the browser, confirm the inspector opens without a separate click; drag-move an operation, confirm Undo becomes enabled and undoes it; trigger Optimize, confirm Undo also becomes enabled (currently does not — FIX-12 finding).
- **Risk**: low — no rendering changes, purely wiring.

### Slice 1 — Gantt extraction from `FiniteSchedule.razor` (Phase 0 item 1; fixes FIX-13)
- **Goal**: move grid/lane/bar/SVG markup into `WorkbenchGantt.razor` + child components per ARC-001, with no behavior change yet.
- **Existing files affected**: `FiniteSchedule.razor` shrinks to page orchestration + non-Gantt panels.
- **New components**: `WorkbenchGantt.razor`, `GanttResourceGrid.razor`, `GanttTimelineViewport.razor`, `GanttResourceLane.razor`, `GanttOperationLayer.razor` at minimum; others can follow in later slices as their layer is built.
- **JS**: `planning-workbench.js` moves/renames alongside, no behavior change in this slice.
- **Tests**: `PlanningWorkbenchMarkupTests.cs` will need path updates (it currently asserts against `FiniteSchedule.razor` string content directly — several assertions will need to move to assert against the new component files instead).
- **Runtime verification**: full visual diff against this document's screenshot set — every screenshot scenario should render pixel-identical before/after.
- **Risk**: medium (large mechanical diff) but low semantic risk if done as a pure move.
- **Depends on**: Slice 0 (cleaner to consolidate state before splitting the component that reads it).

### Slice 2 — Fixed drag lifecycle (Phase 0 items 6, 8; fixes FIX-01/02/03/04/05)
- **Goal**: grab-offset-preserving drag, ghost element, real eligibility-based lane highlighting, configurable snap, 2D autoscroll, Escape-to-cancel.
- **Existing files affected**: `planning-workbench.js` (largely rewritten), `GanttOperationLayer.razor`/new `GanttProposalLayer.razor`.
- **Backend changes**: none required for the *mechanic* — eligibility data (`PlanningOperationResourceOptionView`) already exists and needs to be passed into the drag-start payload from C# to JS (a wiring change, not a new endpoint).
- **Tests**: new JS-level or Playwright-level drag tests; existing `PlanningWorkbenchCommandTests.cs` unaffected (backend untouched).
- **Runtime verification**: repeat this session's drag screenshots (§6) — grab the middle of a bar and confirm it doesn't jump to the cursor; confirm a source-bar-in-place + ghost; confirm an EAF operation dragged over CCM-1 shows a no-drop affordance instead of a valid-looking highlight; confirm Escape cancels mid-drag.
- **Risk**: medium — this is the highest-visibility slice; get FIX-01/02/03 right before adding anything else on top.
- **Depends on**: Slice 1.

### Slice 3 — Dual-tier time scale + real grid/timeline split (Phase 0 items 3, 5; fixes FIX-07/08)
- **Goal**: resizable/persisted resource-column width; two synchronized scale tiers.
- **Existing files affected**: replaces the hardcoded `grid-cols-[176px_1fr]` and `AxisTicks()`.
- **New components**: `GanttTimeScale.razor`, splitter mechanic (JS or CSS resize).
- **Tests**: new markup tests asserting no hardcoded `176px` remains.
- **Runtime verification**: resize the column, reload, confirm width persists; check tick labels at each zoom level show two tiers.
- **Depends on**: Slice 1.

### Slice 4 — Adaptive operation cards + tooltip (Phase 0 item 7; fixes FIX-10)
- **Goal**: content-adapts-to-pixel-width bars; full detail moves to a real tooltip/hover component.
- **Existing files affected**: `GanttOperationLayer.razor`, new `GanttTooltip.razor`.
- **Runtime verification**: repeat the 5 zoom-level screenshots; confirm 7-day zoom no longer shows unreadable slivers with crushed text, and confirm hovering surfaces full detail.
- **Depends on**: Slice 1.

### Slice 5 — Row/time virtualization (Phase 0 item 4; addresses the perf finding in §4.1.2)
- **Goal**: replace the uncached `VisibleLanes`/`FindDetail`/`FindOperation` per-render scan pattern with memoized/virtualized lookups.
- **Existing files affected**: whatever `GanttResourceLane.razor`/`GanttTimelineViewport.razor` became in Slice 1.
- **Tests**: a scaled synthetic dataset (hundreds to low-thousands of operations) added to the test suite to catch regressions before the 10,000-operation acceptance test (25.5.1) is attempted.
- **Runtime verification**: measure render time before/after against a seeded large scenario; this is the one slice where a rough perf number should be captured and recorded, not asserted from first principles.
- **Depends on**: Slice 1; benefits from running after Slice 2-4 so virtualization doesn't have to be redone once the ghost/tooltip/adaptive-card layers exist.

### Slice 6 — Read-model extensions (DTO-001/002/003/005; backend prerequisite for Slices 7-9)
- **Goal**: ship the projection additions identified in §10 that are one join away from existing domain data (dependency link detail, slack fields once the solver exposes them, resource hierarchy fields).
- **Existing files affected**: `PlanningWorkbenchContracts.cs`, `PlannerWorkspaceQueryService.Workbench.cs`.
- **Tests**: extend `PlanningWorkbenchQueryTests.cs`.
- **Risk**: DTO-002 (slack) may require solver-side work beyond a read-model join, depending on whether the CP-SAT model already computes slack internally — this needs a scoping look at `APS.Planning` before estimating, and is flagged as the one slice in this decomposition that could expand scope significantly.

### Slice 7 — Routed dependency layer (Phase 1 item 3; fixes FIX-09)
- **Depends on**: Slice 6 (DTO-001) for real link semantics; can ship a purely-visual routing improvement over the existing straight-line data sooner if DTO-001 slips, but should not claim lag/type support until then.

### Slice 8 — Binding chain / slack explanation (Phase 1 item 4; fixes FIX-06 for real)
- **Depends on**: Slice 6 (DTO-002). Until this lands, "Tight chain" should be relabeled or removed per §8, not left as-is under a name implying criticality.

### Slice 9 — Resource hierarchy grid + capacity panel (Phase 1 items 1, 6; DTO-005/007)
- **Depends on**: Slice 6 (DTO-005) and a new DTO-007 aggregation.

### Slice 10 — Baseline comparison modes (Phase 1 item 5; CMP-*, DTO-006)
- **Depends on**: DTO-006 read-model work, scoped separately since it's a comparison-specific data shape, not a join over existing tables.

### Not sliced here (explicitly out of scope for this decomposition)
- Everything in the requirements doc's Phase 2/3 lists (pin/unpin, keyboard move mode, deep comparison subrows,
  dynamic data-window API, export, touch/RTL) — correctly deferred; no code today provides scaffolding for
  any of them that would change how earlier slices should be built.

---

## 13. Do-not-touch-yet areas

- `PlanningWorkbenchCommandService.ValidateMoveAsync`/`ApplyMoveAsync` — correct and tested; only *consume*
  it more fully from the UI, do not modify its constraint logic as part of Gantt work.
- `PlannerWorkspaceQueryService.Workbench.cs`'s existing aggregation — efficient, no N+1; extend with new
  projections (§10) rather than restructuring what's there.
- The five `PlanningWorkbench/*.razor` chrome components — already clean, already tested by
  `PlanningWorkbenchMarkupTests.cs`; the Gantt extraction (Slice 1) should not need to touch them.
- The proposal→validate→impact→apply→replan command flow itself (§9 item 3) — this is a KEEP item; only its
  Gantt-side presentation changes.
- `PlanContextBar.razor` — dead but not urgent; flagged in §4.1.3 and §7 as a decision point (delete vs.
  revive as the compact plan-context strip), not something to silently resolve inside a Gantt slice.

---

## 14. Risks and open questions

1. **DTO-002 (slack) may require solver-level work**, not just a read-model projection — this is the one
   item in the whole decomposition whose size is genuinely unknown without reading `APS.Planning`'s CP-SAT
   model directly, which was out of scope for this pass (this pass focused on the Gantt UI/read-model/command
   layers, per the user's task framing).
2. **The 10,000-operation performance acceptance test (25.5.1)** has no current baseline measurement — §4.1.2's
   perf finding is a code-reading-derived risk assessment, not a measured number, because the seeded local
   database only has 30 operations. A synthetic large-scenario perf baseline should be captured early in
   Slice 5, not assumed.
3. **Baseline/comparison (CMP-*) has essentially zero current implementation** beyond a UI toggle that changes
   nothing visible — larger scope than its single line in the FIX list might suggest.
4. **`WorkbenchAnalysisDock`'s "Compare" tab** (`WorkbenchAnalysisDock.razor:37-38`) already links out to
   `/decide/compare` — that route was not audited in this pass; it may already contain comparison logic that
   should inform CMP-* design rather than starting from zero. Worth a follow-up look before Slice 10.

---

## 15. Definition-of-done cross-check

This document does not re-derive the requirements doc's own Section 29 (20-item definition-of-done) or
Section 25 (acceptance tests 25.1-25.6) — both remain authoritative as written there. Every code finding in
this document was written to be directly checkable against those items (e.g., FIX-01/02/03 map directly to
DND-* acceptance criteria; the perf finding in §4.1.2 maps directly to acceptance test 25.5.1). No conflicts
between this document's findings and the requirements doc's acceptance criteria were found.

---

## 16. Verification record

- **Branch/ref inspected**: `claude/project-status-review-o2dx1j`.
- **Commit inspected**: `2f159a4ba1ce3b7d9b64d12b74df28e4515b7105`.
- **Build**: `dotnet build src/APS.Service/APS.Service.csproj -c Debug` — succeeded, 0 warnings, 0 errors.
  (`APS.DesktopHost` was not built — it targets Windows/WPF and cannot build on this Linux session; this is
  expected and not a defect.)
- **Tests run**: `dotnet test tests/APS.Planning.Tests` (158 passed) and `dotnet test tests/APS.UI.Tests`
  (53 passed) — 211/211 passed, 0 failed, 0 skipped.
- **Runtime inspected**: yes — `APS.Service` launched locally against the pre-existing local database
  (`/root/.local/share/APS-Data/Data/aps.db`, untouched/not reseeded), driven via a scripted Playwright
  session in the pre-installed Chromium browser, then stopped cleanly.
- **Screenshots captured**: 17, listed in §5, held in the session scratchpad (not committed to the repository).
- **Document path**: `docs/current/APS_GANTT_IMPLEMENTATION_RECONNAISSANCE.md` (this file).
- **Commit SHA**: recorded after this file is committed (see the commit created immediately after this pass, if the working tree was otherwise clean).

No application code was modified. No database was altered or reseeded. No implementation of the Gantt
overhaul has begun.
