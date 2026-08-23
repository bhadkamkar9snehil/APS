# Unified Planner Lifecycle Workbench Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current schedule-renderer page with the state-aware workbench in which planners create scenarios, form campaigns, iterate, inspect impact, release, monitor execution, and start recovery planning.

**Architecture:** Preserve the canonical planning lifecycle and immutable Plan Versions. Add explicit UI workflow state over the existing aggregate workbench read model, split the monolithic Razor page into focused regions, and make the same Gantt selection persist across Plan, Campaigns, Execution, and Recovery modes. Execution remains read-only until recovery creates a child scenario.

**Tech Stack:** .NET 10, Blazor Razor Class Library, WPF BlazorWebView, EF Core SQLite, Tailwind CSS 4, xUnit.

**Spec:** `docs/superpowers/specs/2026-08-21-planning-workbench-design.md`

## Global Constraints

- Preserve the installed SQLite database and all historical Plan Versions.
- Never mutate a released plan; changes create persisted child scenarios.
- Keep Planning Workbench as the default route and first navigation item.
- Do not expose internal database identifiers.
- Do not use blue or cyan theme accents.
- Hide dependency lines by default and render only a focused selected chain.
- Do not publish a release until the complete workbench flow is verified.
- Do not use Computer Use for verification.

---

### Task 1: Lifecycle vocabulary and deterministic workbench state

**Files:**
- Modify: `src/APS.UI/State/PlanningWorkbenchState.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs`

**Interfaces:**
- Produces: `PlanningWorkbenchMode`, `PlanningScenarioIntent`, focused-chain visibility, analysis-dock state, and legal state transitions.
- Consumes: existing persisted plan status and selection state.

- [ ] Write failing tests proving the default mode is Plan, dependencies are hidden, released plans are read-only, Recovery mode creates an editable intent, and selection survives mode/lens changes.
- [ ] Run `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter PlanningWorkbenchStateTests` and verify the new assertions fail for missing state.
- [ ] Implement the minimal deterministic state transitions.
- [ ] Re-run the focused tests and verify they pass.

### Task 2: Workbench composition and navigation hierarchy

**Files:**
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor`
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchAnalysisDock.razor`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/Components/Layout/MainLayout.razor`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- Consumes: `PlanningWorkbenchView`, `PlanningWorkbenchState`, plan status, and existing callbacks.
- Produces: scenario context, mode rail, primary actions, analysis tabs, and simplified supporting navigation.

- [ ] Write failing markup tests for the four lifecycle modes, scenario action hierarchy, execution/recovery language, analysis dock, and secondary Setup navigation.
- [ ] Run the focused UI tests and confirm failure because the components do not exist.
- [ ] Implement the three components and compose them into the page without changing planning authority.
- [ ] Re-run focused and full UI tests.

### Task 3: Readable schedule and contextual dependency focus

**Files:**
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/wwwroot/planning-workbench.js`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- Consumes: visible window, selected planning key, resource lanes, and predecessor relationships.
- Produces: minimum readable operation width, compact lane density, selected-chain dependencies, and intentional empty space.

- [ ] Write failing markup tests proving dependencies require a selected operation, labels have a readable fallback, and the canvas owns the central flexible area.
- [ ] Run the focused tests and confirm the existing global dependency layer fails the contract.
- [ ] Restrict edges to the selected chain, add zoom/readability rules, and remove redundant page chrome.
- [ ] Rebuild Tailwind CSS and run UI tests.

### Task 4: Scenario, execution, and recovery action gates

**Files:**
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/State/PlanningWorkbenchState.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`

**Interfaces:**
- Consumes: `PlanContextView.IsReleased`, `PlanVersionStatus`, existing lifecycle/release services.
- Produces: legal action availability and explicit recovery entry from released execution.

- [ ] Write failing tests for released-plan read-only controls, create-recovery availability, draft save/optimize/validate availability, and release gating.
- [ ] Verify focused tests fail.
- [ ] Implement action gates and recovery intent without mutating the released plan.
- [ ] Run focused and full tests.

### Task 5: Data compatibility and complete verification

**Files:**
- Test: `tests/APS.UI.Tests/UserFacingIdentifierContractTests.cs`
- Test: `tests/APS.Planning.Tests/PlanningWorkbenchPersistenceTests.cs` only if a persistence gap is discovered.

**Interfaces:**
- Guarantees: unchanged existing database, historical Plan Versions, no user-facing UUIDs, and restart-safe current plan selection.

- [ ] Back up the local database and record integrity and row counts.
- [ ] Run all planning and UI tests.
- [ ] Build the desktop host and publish a local release candidate without tagging or uploading it.
- [ ] Launch it and verify startup, workbench queries, static assets, database counts, and unhandled errors through logs.
- [ ] Recheck database integrity and counts after launch.
