# APS Gantt Workbench Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current page-specific schedule drawing with a planner-grade APS finite-capacity Gantt that owns synchronized resource/time geometry, safe direct manipulation, APS-native planning layers, capacity context, accessibility, and verified desktop/runtime behavior.

**Architecture:** `FiniteSchedule.razor` remains page orchestration, while `Components/PlanningWorkbench/Gantt/` owns the control. A deterministic `GanttViewportState` is the only time/pixel coordinate engine; `PlanningWorkbenchState` composes viewport, selection, proposal, layer, drawer, preference, and persisted Plan-Version history state. JavaScript owns pointer capture and frame-rate geometry only; authoritative eligibility, validation, repair, persistence, release, and execution semantics stay in .NET.

**Tech Stack:** .NET 10, Blazor Razor Class Library, WPF BlazorWebView, ASP.NET Core Blazor Server, EF Core SQLite, Tailwind CSS 4 standalone CLI, JavaScript pointer events, xUnit.

**Spec:** `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md`

## Global Constraints

- `docs/APS_GANTT_WORKBENCH_OVERHAUL_REQUIREMENTS.md` outranks the benchmark and older cockpit documents.
- Start from `origin/claude/project-status-review-o2dx1j`; do not merge into or modify `main`.
- Preserve configured manufacturing routes, eligible-resource truth, immutable released Plan Versions, execution protection, and canonical solver/replan authority.
- Do not install DHTMLX, Node/npm, generic project-task editing, JavaScript planning truth, fabricated planning data, or historical Python/workbook behavior.
- Edit `src/APS.UI/wwwroot/tailwind-input.css`; generated `tailwind.css` is build output.
- Build, test, service, browser, and desktop verification are local; GitHub Actions are not evidence.
- Do not reset/reseed the local database or destroy historical Plan Versions.
- Use TDD for deterministic behavior and add browser interaction proof for pointer/rendering behavior.

---

### Task 1: Authoritative viewport and interaction state

**Files:**
- Create: `src/APS.UI/State/GanttViewportState.cs`
- Create: `src/APS.UI/State/GanttInteractionModels.cs`
- Modify: `src/APS.UI/State/PlanningWorkbenchState.cs`
- Modify: `src/APS.UI/State/PlannerCockpitState.cs`
- Test: `tests/APS.UI.Tests/GanttViewportStateTests.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs`

**Interfaces:**
- Produces: `GanttViewportState.Configure(planStartUtc, planEndUtc, contentStartUtc, contentEndUtc, timelineWidthPx)`, `TimeToX`, `XToTime`, `Clip`, `Snap`, `ZoomAt`, `Fit`, `ResetFit`, `Pan`, `GridWidthPx`, `Density`, `SnapMode`, and collapsed-group state.
- Produces: `GanttSnapMode`, `GanttDensity`, `GanttFitScope`, `GanttClipResult`, `GanttScaleTier`, and semantic `PlanHistoryEntry`.
- Consumes: UTC plan/content ranges and timeline pixel width measured by JavaScript.

- [ ] **Step 1: Write failing viewport tests.** Cover time-to-pixel/inverse round-trip, clipping, five snap policies plus Free, pointer-anchored zoom, fit using real pixel width, exact reset, pan bounds, and repeated zoom drift.
- [ ] **Step 2: Run `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter GanttViewportStateTests` and verify compile/test failure because the types do not exist.**
- [ ] **Step 3: Implement `GanttViewportState` with UTC tick arithmetic and a single transform.** `TimeToX` and `XToTime` must be inverse within one tick at unclamped points; clipping returns `IsVisible`, clipped bounds, x, and width without imposing a fake duration minimum.
- [ ] **Step 4: Run the focused viewport tests and verify they pass.**
- [ ] **Step 5: Write failing state tests proving selection opens the real inspector owner, proposal/history has one owner, Optimize/move history is semantic, and transient state clears without deleting persisted history.**
- [ ] **Step 6: Consolidate duplicated queue/inspector/history fields.** `PlanningWorkbenchState` owns schedule interaction/history; `PlannerCockpitState` owns drawer/dock presentation and exposes `OpenInspector()` for selection synchronization. Remove page-private undo/redo stacks.
- [ ] **Step 7: Run all UI state tests and commit `refactor(gantt): establish authoritative viewport and state`.**

### Task 2: Gantt read-model truth

**Files:**
- Modify: `src/APS.Application/PhysicalWorkspaceContracts.cs`
- Modify: `src/APS.Application/PlanningWorkbenchContracts.cs`
- Modify: `src/APS.Infrastructure/PlannerWorkspaceQueryService.Physical.cs`
- Modify: `src/APS.Infrastructure/PlannerWorkspaceQueryService.Workbench.cs`
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchQueryTests.cs`

**Interfaces:**
- Produces: `ScheduleResourceLaneView` hierarchy fields (`PlantId`, `PlantCode`, `PlantName`, `AreaId`, `AreaCode`, `AreaName`, `ProcessStageId`, `ProcessStageCode`, `ProcessStageName`, `DisplayOrder`).
- Produces: `PlanningDependencyLinkView`, `PlanningResourceCalendarIntervalView`, `PlanningBaselinePlacementView`, and `PlanningCapacityBucketView` collections on `PlanningWorkbenchView`.
- Capacity buckets report time range, available minutes, processing minutes, unavailable minutes, occupancy ratio, and basis; they do not fabricate setup/changeover when absent.

- [ ] **Step 1: Extend the in-memory query fixture with plant, area, stage, an unavailable calendar interval, a predecessor, a baseline operation, and a second time bucket; assert exact projected hierarchy, calendar, dependency, baseline, and load facts.**
- [ ] **Step 2: Run `dotnet test tests/APS.Planning.Tests/APS.Planning.Tests.csproj --filter PlanningWorkbenchQueryTests` and verify the new assertions fail.**
- [ ] **Step 3: Add the contracts and batched EF projections.** Dependency kind is truthful `FinishStart/Routing` with known lag fields nullable until canonical data exists. Capacity derives only from scheduled occupancy plus actual calendar/operating state. Baseline placements come from the selected persisted baseline plan.
- [ ] **Step 4: Add tests for unchanged baseline placements and resource-changed placements remaining on the original lane.**
- [ ] **Step 5: Run focused and full planning tests; commit `feat(gantt): project hierarchy calendar baseline and capacity truth`.**

### Task 3: Extract the reusable synchronized Gantt control

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceGrid.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimeScale.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimelineViewport.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceLane.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttModels.cs`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- `WorkbenchGantt` consumes `PlanningWorkbenchView`, `PlanningWorkbenchState`, editability/fence inputs, and callbacks for selection and staged moves.
- `GanttModels.BuildScene` pre-indexes operation detail, resource, baseline, dependency, and business identity lookups once per data/state change.
- The grid and timeline render the same ordered `GanttRowModel` list and use CSS variables `--aps-gantt-grid-width` and `--aps-gantt-row-height`.

- [ ] **Step 1: Add failing component-architecture tests requiring the new files, forbidding `grid-cols-[176px_1fr]` and Gantt bar/SVG markup in `FiniteSchedule.razor`, and asserting no `Tight chain` label remains.**
- [ ] **Step 2: Run the focused UI tests and verify failure.**
- [ ] **Step 3: Extract cached scene construction and the synchronized grid/timeline shell without changing plan commands.** Resource groups derive from projected plant/area/process-stage metadata and collapse view-only.
- [ ] **Step 4: Render true grid columns for resource, state, occupied hours, utilization, operation count, and next operation using only projected facts.**
- [ ] **Step 5: Render a dual-tier scale from viewport state and time-accurate clipped operation blocks with adaptive pixel-width content and accessible names.**
- [ ] **Step 6: Run UI tests and solution build; inspect the running screen for row/header alignment; commit `refactor(gantt): extract synchronized grid timeline control`.**

### Task 4: Splitter, navigation, preferences, and virtualization

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttPreferencesStore.cs`
- Modify: `src/APS.UI/wwwroot/planning-workbench.js`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Modify: Gantt components from Task 3
- Test: `tests/APS.UI.Tests/GanttViewportStateTests.cs`
- Test: `tests/APS.UI.Tests/GanttSceneTests.cs`

**Interfaces:**
- JavaScript reports timeline width/scroll position at meaningful transitions and implements splitter, empty-space pan, wheel zoom anchor, synchronized vertical scroll, and requestAnimationFrame updates.
- `.NET` receives `UpdateViewportMetrics`, `ZoomAt`, `SetGridWidth`, and `SetVisibleRowRange`; it does not receive per-pointermove operation drag updates.

- [ ] **Step 1: Write failing tests for 220 px minimum, 320 px default, 45% maximum, fit after resize, named Detail/Shift/Day/3 Days/Week/2 Weeks/Month levels, and visible-time/row filtering with overscan.**
- [ ] **Step 2: Verify focused test failure.**
- [ ] **Step 3: Implement the splitter and local preference persistence for grid width, columns, density, zoom, snap, collapsed groups, layers, and capacity panel height.**
- [ ] **Step 4: Implement pointer-anchored Ctrl-wheel zoom, empty-background pan, Fit all/visible/selection/campaign/order/date-range APIs, and Reset Fit.** Expose only the compact high-frequency toolbar actions.
- [ ] **Step 5: Implement row/time clipping so normal views mount under the explicit DOM budget; preserve stable planning keys across mount/unmount.**
- [ ] **Step 6: Run focused/full UI tests and browser checks for resize, zoom anchor, pan, Fit/reset, row alignment, and panel stability; commit `feat(gantt): add mature viewport navigation and virtualization`.**

### Task 5: Proposal-based drag engine

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttProposalLayer.razor`
- Modify: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor`
- Modify: `src/APS.UI/wwwroot/planning-workbench.js`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Test: `tests/APS.UI.Tests/GanttDragGeometryTests.cs`
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchCommandTests.cs`

**Interfaces:**
- Produces: pure `GanttDragGeometry.CandidateStart(pointerTime, grabOffset, snapMode, shiftBoundaries)` and eligibility/restriction decisions tested in .NET.
- JS drag-start payload contains planning key, source interval/resource, duration, eligible resource IDs, protected state, snap mode, and viewport timestamps.
- JS invokes `.NET StageDraggedMove(planningKey, resourceId, targetStartUtc)` only on eligible drop; Escape/pointercancel/blur invoke no validation.

- [ ] **Step 1: Write failing tests for grabs at 0%, 50%, and 70%; every snap mode; ineligible resources; running/completed protection; and frozen policy exposure.**
- [ ] **Step 2: Verify red failures.**
- [ ] **Step 3: Implement thresholded pointer capture with a cloned proposal ghost while the source stays fixed/dimmed.**
- [ ] **Step 4: Implement eligible/ineligible/checking lane affordances from canonical resource options, target start/end/delta feedback, shared snap guide, proportional 2D autoscroll, and immediate Escape/pointercancel/blur cleanup.**
- [ ] **Step 5: Keep drop staged through `PlanningMoveProposal -> ValidateMoveAsync -> impact -> ApplyMoveAsync -> persisted child Plan Version`; invalid drops explain rejection and never mutate the plan.**
- [ ] **Step 6: Run unit/planning tests and browser drag acceptance at start/middle/end, same/alternate/invalid lane, frozen/running/completed, autoscroll, and Escape; commit `feat(gantt): rebuild safe proposal drag lifecycle`.**

### Task 6: APS planning layers and comparison

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttBaselineLayer.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttDependencyLayer.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttMarkerLayer.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttCalendarLayer.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttCampaignLayer.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttExecutionLayer.razor`
- Modify: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimelineViewport.razor`
- Test: `tests/APS.UI.Tests/GanttSceneTests.cs`

**Interfaces:**
- Every layer consumes `GanttViewportState`/`GanttScene` geometry; no layer calculates percentages independently.
- Dependencies are focused, readonly, orthogonally routed, arrowed, typed, and clipped with continuation cues.
- Baseline supports ghost overlay and changed-only immediately; compare subrow is enabled when row density allows.

- [ ] **Step 1: Write failing scene tests for original-resource baseline, added/removed semantics, routed dependency ports, focused chain, marker density, lane-specific calendar intervals, fence distinction, campaign span, and actual/planned geometry.**
- [ ] **Step 2: Verify test failure.**
- [ ] **Step 3: Implement baseline, selected-chain dependency, marker/fence, calendar, campaign, and execution layers using the shared transform.**
- [ ] **Step 4: Remove temporal-adjacency criticality.** Show `Binding chain unavailable` as a truthful disabled mode until solver-derived slack/category data exists; never relabel the heuristic.
- [ ] **Step 5: Run UI/planning tests and visually verify layer alignment through zoom, splitter resize, scrolling, selection, light/dark themes; commit `feat(gantt): add APS planning and comparison layers`.**

### Task 7: Capacity, workbench integration, keyboard, and performance

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttCapacityPanel.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/Gantt/GanttAccessibleSchedule.razor`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor`
- Modify: `src/APS.UI/Components/PlanningWorkbench/WorkbenchAnalysisDock.razor`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Test: `tests/APS.UI.Tests/GanttAccessibilityTests.cs`
- Test: `tests/APS.UI.Tests/GanttSceneTests.cs`

**Interfaces:**
- Capacity panel shares the viewport x transform and click-focuses a resource/time bucket and contributing operations.
- The Gantt has one tab entry and internal roving focus; accessible schedule table shares selection.

- [ ] **Step 1: Add failing tests for capacity bucket geometry/click focus, one Gantt tab stop, accessible operation names, roving navigation, Escape priority, keyboard fit/pan, and forced-colors CSS.**
- [ ] **Step 2: Verify red failures.**
- [ ] **Step 3: Implement collapsible/resizable load panel with truthful processing/unavailable/idle categories and zoom-adaptive buckets.**
- [ ] **Step 4: Consolidate scenario/lifecycle/toolbar chrome to the vertical budget while preserving queue, inspector, impact tray, and analysis dock as modeless surfaces that do not alter viewport.**
- [ ] **Step 5: Implement keyboard navigation, context menu, accessible table, visible focus, fullscreen mode, and light/dark/high-contrast semantics.** Disabled commands explain genuine backend prerequisites; no fake actions.
- [ ] **Step 6: Profile the largest local plan and a deterministic 10,000-operation synthetic scene.** Record mounted bar/primitive counts, interaction timing, and fix avoidable scans/render churn.
- [ ] **Step 7: Run focused/full tests and commit `feat(gantt): integrate capacity accessibility and planner shell`.**

### Task 8: Runtime proof, documentation, and branch handoff

**Files:**
- Create: `docs/current/APS_GANTT_WORKBENCH_IMPLEMENTATION_STATUS.md`
- Modify: `docs/current/README.md`
- Modify: implementation/tests only when verification finds defects

**Interfaces:**
- Produces: requirement/status/evidence/gap matrix and final source-control/runtime handoff.

- [ ] **Step 1: Run `dotnet restore APS.slnx`, `dotnet build APS.slnx`, planning tests, UI tests, and any added interaction/performance tests from a clean build.**
- [ ] **Step 2: Start `APS.Service`, verify `/api/health`, open the real Planning Workbench with the existing SQLite data, and exercise the complete layout/navigation/drag/proposal/selection/comparison/execution/theme/resize checklist.**
- [ ] **Step 3: Launch `APS.DesktopHost` and verify the Planning Workbench opens with real schedule data and no startup/runtime errors.**
- [ ] **Step 4: Capture final screenshots and browser-console evidence at practical desktop sizes and both themes.**
- [ ] **Step 5: Inspect SQLite integrity and preserve historical Plan-Version counts; do not reset/reseed.**
- [ ] **Step 6: Update the implementation-status matrix with exact evidence and classify only genuine residual gaps as P0/P1/P2/P3/backend prerequisite.**
- [ ] **Step 7: Review `git status`, `git diff --stat`, and `git diff`; remove abandoned prototypes/build artifacts; commit coherent final checkpoint(s).**
- [ ] **Step 8: Push `codex/gantt-workbench-overhaul`, verify the remote ref and commit list, then perform the full definition-of-done audit before marking the goal complete.**
