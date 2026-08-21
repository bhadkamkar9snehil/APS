# APS Planning Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete Gantt-first PPC Planning Workbench, including safe interactive replanning, comparison, exceptions, campaign/material context, persistence, and release.

**Architecture:** Extend the existing canonical workspace read model with one aggregated workbench view and add an application-level command service that validates staged moves and delegates accepted changes to `IPlanningLifecycleService.ReplanAsync`. Keep historical Plan Versions immutable. Implement the UI as focused Razor components coordinated by a workbench state object and tested through deterministic state/service tests plus markup contracts.

**Tech Stack:** .NET 10, Blazor Razor Class Library, WPF BlazorWebView desktop host, EF Core SQLite, Tailwind CSS 4, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-planning-workbench-design.md`

## Global Constraints

- Deliver the entire workbench in one release, not as partially exposed product slices.
- Preserve the existing local database and all historical Plan Versions.
- Released plans are immutable; accepted interactions create persisted replans.
- No internal IDs, blue, or cyan are user-facing.
- Do not use Computer Use for verification.

---

### Task 1: Workbench contracts and aggregation

**Files:**
- Create: `src/APS.Application/PlanningWorkbenchContracts.cs`
- Create: `src/APS.Infrastructure/PlannerWorkspaceQueryService.Workbench.cs`
- Modify: `src/APS.Application/PlannerWorkspaceContracts.cs`
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchQueryTests.cs`

**Interfaces:**
- Produces: `PlanningWorkbenchView`, `PlanningQueueView`, `PlanningWorkbenchException`, `PlanningWorkbenchMaterialPool`, and `IPlannerWorkspaceQueryService.GetPlanningWorkbenchAsync(Guid?, Guid?, CancellationToken)`.

- [ ] Write a failing test that seeds a plan and asserts the aggregate contains plan context, demand, campaigns, resource lanes, material pools, exceptions, and optional comparison.
- [ ] Run the focused test and verify it fails because the contract is absent.
- [ ] Implement the aggregation using the existing partial query-service read models without duplicating planning truth.
- [ ] Run the focused test and full planning tests.
- [ ] Commit the contracts and query implementation.

### Task 2: Planning proposal validation and application

**Files:**
- Create: `src/APS.Application/PlanningWorkbenchCommandContracts.cs`
- Create: `src/APS.Infrastructure/PlanningWorkbenchCommandService.cs`
- Modify: `src/APS.Infrastructure/ServiceCollectionExtensions.cs`
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchCommandTests.cs`

**Interfaces:**
- Produces: `PlanningMoveProposal`, `PlanningProposalImpact`, `PlanningConstraintFinding`, `IPlanningWorkbenchCommandService.ValidateMoveAsync`, and `ApplyMoveAsync`.
- Consumes: `IPlanningLifecycleService.ReplanAsync` with `OperationResourceOverride`, time-fence policy, baseline ID, and reference time.

- [ ] Write failing tests for eligible moves, ineligible resources, frozen-zone moves, overlaps, and an accepted move creating a child Plan Version.
- [ ] Verify the tests fail before implementation.
- [ ] Implement validation from persisted schedule/master/material facts and map accepted moves to canonical replan input.
- [ ] Run focused and full planning tests.
- [ ] Commit the command service.

### Task 3: Workbench interaction state and command history

**Files:**
- Create: `src/APS.UI/State/PlanningWorkbenchState.cs`
- Create: `src/APS.UI/State/PlanningWorkbenchPreferences.cs`
- Modify: `src/APS.Service/Program.cs`
- Modify: `src/APS.DesktopHost/App.xaml.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs`

**Interfaces:**
- Produces: lens, selection, viewport, zoom, layers, collapsed groups, staged proposal, impact, history, and undo/redo state.

- [ ] Write failing tests for selection, lens preservation, zoom bounds, staging, applying, rejecting, undo, redo, and serialized preferences.
- [ ] Run them and confirm failure.
- [ ] Implement deterministic state transitions and scoped service registration.
- [ ] Run UI tests.
- [ ] Commit workbench state.

### Task 4: Complete Gantt canvas

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchGantt.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/GanttTimeline.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/GanttResourceLane.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/GanttOperationBlock.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/GanttCampaignSpan.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/GanttDependencyLayer.razor`
- Create: `src/APS.UI/wwwroot/planning-workbench.js`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- Consumes: `PlanningWorkbenchView` and `PlanningWorkbenchState`.
- Produces: selection, keyboard actions, pan/zoom, drag proposal, fit, group collapse, and accessible schedule-table events.

- [ ] Write markup contract tests for semantic labels, keyboard targets, resource grouping, baseline ghosts, time fences, downtime, campaign spans, and operation status.
- [ ] Verify failure.
- [ ] Implement time-window clipping, lane virtualization, synchronized scrolling, zoom, drag ghosts, snapping, and keyboard operation without introducing a Node dependency.
- [ ] Run UI tests and build Tailwind output.
- [ ] Commit the Gantt canvas.

### Task 5: Workbench shell, queue, inspector, and exception dock

**Files:**
- Replace: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchHeader.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchToolbar.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/PlanningQueue.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/PlanningInspector.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/PlanningImpactDock.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/PlanningReleasePanel.razor`
- Modify: `src/APS.UI/Components/Layout/MainLayout.razor`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- Consumes: workbench query/command services and workbench state.
- Produces: end-to-end Calculate, Compare, Validate, Repair, Apply/Reject, Undo/Redo, Save, and Release interactions.

- [ ] Extend failing markup tests to cover every workbench region, lens, queue category, contextual action, impact state, and release gate.
- [ ] Implement the complete shell and connect all working commands.
- [ ] Make unavailable domain commands explicitly disabled with a reason rather than fake controls.
- [ ] Run UI and planning tests.
- [ ] Commit the complete workbench screen.

### Task 6: Campaign, demand, material, and comparison lenses

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchLensSelector.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/DemandLens.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/CampaignLens.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/MaterialLens.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/ExceptionLens.razor`
- Modify: `src/APS.UI/Components/PlanningWorkbench/WorkbenchGantt.razor`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- All lenses consume the aggregate workbench view and preserve `PlannerEntityRef` selection.

- [ ] Add failing tests for each lens, lineage highlighting, material balance, campaign hierarchy, and changed-only comparison.
- [ ] Implement the lenses and baseline overlay.
- [ ] Run UI and planning tests.
- [ ] Commit the integrated lenses.

### Task 7: Persistence, compatibility, and regression protection

**Files:**
- Modify: persistence files only if command-history assumptions require stored data beyond existing Plan Version snapshots.
- Create migration only when additive schema is proven necessary.
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchPersistenceTests.cs`
- Test: `tests/APS.UI.Tests/UserFacingIdentifierContractTests.cs`

**Interfaces:**
- Guarantees existing databases open without destructive migration and all applied operations remain historical Plan Versions.

- [ ] Back up and inspect the local database schema and row counts before migration testing.
- [ ] Write tests that open a pre-workbench database, query historical plans, apply a replan, restart services, and recover the selected plan.
- [ ] Add only additive persistence changes if required.
- [ ] Verify no user-facing UUIDs and no destructive seed path.
- [ ] Commit compatibility protection.

### Task 8: Full verification and desktop release candidate

**Files:**
- Modify release metadata only after behavior is verified.

- [ ] Run all planning and UI tests.
- [ ] Build the solution and publish the desktop host.
- [ ] Start the service host and verify workbench routes, logs, static assets, database counts, query endpoints, and interactive-circuit errors without Computer Use.
- [ ] Verify the existing database before and after the run.
- [ ] Package and launch the desktop release candidate.
- [ ] Inspect Git status and commit all intended work.
