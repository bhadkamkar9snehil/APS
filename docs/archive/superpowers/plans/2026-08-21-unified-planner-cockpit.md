# Unified Planner Cockpit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace APS page navigation with a desktop menu and make one adaptive, full-screen Gantt cockpit host planning, analysis, execution, traceability, and setup access without losing scenario context.

**Architecture:** `MainLayout` becomes a thin menu shell. `FiniteSchedule` remains the default workspace and composes overlay drawers plus a bottom analysis dock around the Gantt. A small `PlannerCockpitState` owns menu, overlay, and analysis state independently from schedule-command state, while existing routed pages remain reachable as secondary administrative workspaces during the transition.

**Tech Stack:** .NET 10, Blazor Razor components, Tailwind CSS v4 standalone CLI, xUnit, WPF/WebView2 desktop host.

**Spec:** `docs/superpowers/specs/2026-08-21-planning-workbench-design.md`

## Global Constraints

- No persistent sidebar, footer, or global Plan Context strip.
- No blue or cyan theme accent.
- Queue and inspector overlay the Gantt instead of reducing its layout columns.
- Existing SQLite data and historical Plan Versions remain unchanged.
- No tag, installer, or GitHub release without explicit user instruction.
- Push each verified implementation checkpoint to `origin/agent/aps-dotnet-planning-core`.

---

### Task 1: Desktop menu shell

**Files:**
- Create: `src/APS.UI/Components/Layout/DesktopMenuBar.razor`
- Create: `src/APS.UI/Components/Layout/DesktopMenu.razor`
- Modify: `src/APS.UI/Components/Layout/MainLayout.razor`
- Test: `tests/APS.UI.Tests/PlannerCockpitMarkupTests.cs`

**Interfaces:**
- Produces: `DesktopMenuBar` with menu groups `File`, `Plan`, `View`, `Analyze`, `Execute`, `Configure`, and `Help`.
- Consumes: `NavigationManager`, `ThemeService`, and `IUpdateService` from the existing layout.

- [ ] **Step 1: Write the failing shell test**

```csharp
[Fact]
public void Desktop_shell_has_menu_bar_without_sidebar_or_footer()
{
    var layout = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/MainLayout.razor"));
    var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));
    Assert.Contains("<DesktopMenuBar", layout);
    Assert.DoesNotContain("<aside", layout);
    Assert.DoesNotContain("<footer", layout);
    foreach (var label in new[] { "File", "Plan", "View", "Analyze", "Execute", "Configure", "Help" })
        Assert.Contains($"Label=\"{label}\"", menu);
}
```

- [ ] **Step 2: Run `dotnet test tests/APS.UI.Tests/APS.UI.Tests.csproj --filter PlannerCockpitMarkupTests` and confirm failure because the menu components do not exist.**
- [ ] **Step 3: Implement the menu components and replace the sidebar/footer in `MainLayout`. Use button/menu semantics, click selection, Escape dismissal, and routes for existing secondary workspaces.**
- [ ] **Step 4: Run the focused test and confirm it passes.**
- [ ] **Step 5: Commit `feat: replace sidebar with desktop menu shell`.**

### Task 2: Cockpit panel state and overlay drawers

**Files:**
- Create: `src/APS.UI/State/PlannerCockpitState.cs`
- Create: `src/APS.UI/Components/PlanningWorkbench/WorkbenchOverlayDrawer.razor`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/Components/_Imports.razor`
- Test: `tests/APS.UI.Tests/PlannerCockpitStateTests.cs`

**Interfaces:**
- Produces: `PlannerCockpitState.ToggleQueue()`, `ToggleInspector()`, `OpenAnalysis(PlannerAnalysisView)`, `CloseTransientPanels()`, and `ClearFocusPanels()`.
- Consumes: current queue/inspector content from `FiniteSchedule`.

- [ ] **Step 1: Write failing state tests asserting queue and inspector can open independently, Escape closes the top transient panel, and opening either does not change Gantt layout state.**
- [ ] **Step 2: Run the focused state tests and confirm compile failure because `PlannerCockpitState` is absent.**
- [ ] **Step 3: Implement the state and register it as a scoped service in `src/APS.DesktopHost/App.xaml.cs`.**
- [ ] **Step 4: Replace grid columns for queue/inspector with absolute left/right overlay drawers above the Gantt.**
- [ ] **Step 5: Run focused state and markup tests.**
- [ ] **Step 6: Commit `feat: overlay planner queue and inspector`.**

### Task 3: Adaptive full-height Gantt

**Files:**
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/wwwroot/tailwind-input.css`
- Test: `tests/APS.UI.Tests/PlannerCockpitMarkupTests.cs`
- Test: `tests/APS.UI.Tests/PlanningWorkbenchStateTests.cs`

**Interfaces:**
- Produces: CSS custom property `--aps-visible-lanes`, adaptive lane class `aps-gantt-lanes`, and an unpadded full-height workbench root.
- Consumes: `VisibleLanes.Count` from the current read model.

- [ ] **Step 1: Add a failing markup test requiring `aps-gantt-lanes`, `--aps-visible-lanes`, and absence of fixed queue/inspector grid columns.**
- [ ] **Step 2: Run it and confirm failure.**
- [ ] **Step 3: Make the Gantt rows use `grid-auto-rows: clamp(64px, calc((100% - 40px) / var(--aps-visible-lanes)), 104px)` for eight or fewer lanes and 64 px scrolling rows above that threshold.**
- [ ] **Step 4: Remove unused permanent canvas padding and ensure toolbar plus scenario strip stay within the specified vertical budget.**
- [ ] **Step 5: Run focused tests and build `src/APS.UI/APS.UI.csproj`.**
- [ ] **Step 6: Commit `feat: make gantt fill the planner cockpit`.**

### Task 4: Consolidated analysis dock

**Files:**
- Replace: `src/APS.UI/Components/PlanningWorkbench/WorkbenchAnalysisDock.razor`
- Modify: `src/APS.UI/Components/Pages/FiniteSchedule.razor`
- Modify: `src/APS.UI/State/PlannerCockpitState.cs`
- Test: `tests/APS.UI.Tests/PlannerCockpitStateTests.cs`
- Test: `tests/APS.UI.Tests/PlannerCockpitMarkupTests.cs`

**Interfaces:**
- Produces enum `PlannerAnalysisView` with `ControlOverview`, `Exceptions`, `Capacity`, `Delivery`, `Material`, `ScenarioComparison`, `Execution`, and `Traceability`.
- Consumes existing workbench queue, comparison, execution-detail, and selection data; unsupported deep detail links to the existing routed workspace while preserving cockpit state.

- [ ] **Step 1: Add failing tests for one mutually exclusive analysis selection and for all required analysis labels in the dock.**
- [ ] **Step 2: Run and confirm failure.**
- [ ] **Step 3: Implement a 30 px collapsed tab rail and an open dock whose content is selected by `PlannerAnalysisView`.**
- [ ] **Step 4: Implement Control overview summaries and contextual selected-object actions using existing workbench data.**
- [ ] **Step 5: Wire Analyze and Execute menu commands to the dock through a shared scoped `PlannerCockpitState`.**
- [ ] **Step 6: Run focused tests.**
- [ ] **Step 7: Commit `feat: consolidate planner analysis dock`.**

### Task 5: Remove brittle tests and shorten the build loop

**Files:**
- Modify: `tests/APS.UI.Tests/PlanningWorkbenchMarkupTests.cs`
- Modify: `tests/APS.UI.Tests/LayoutThemeContractTests.cs`
- Modify: `src/APS.UI/APS.UI.csproj`
- Test: `tests/APS.UI.Tests/PlannerCockpitMarkupTests.cs`

**Interfaces:**
- Produces: behaviour-oriented cockpit tests and incremental Tailwind inputs covering Razor, `tailwind-input.css`, and theme CSS only.
- Consumes: existing `CompileTailwindCss` MSBuild target.

- [ ] **Step 1: Delete only assertions made obsolete by the removed sidebar, duplicate navigation, fixed queue geometry, and generated CSS class strings. Preserve theme, default-route, lifecycle, release metadata, persistence, and planning tests.**
- [ ] **Step 2: Add a test asserting the Tailwind target has incremental `Inputs` and `Outputs` and does not run during `dotnet test --no-build`.**
- [ ] **Step 3: Update `TailwindSource` inputs to include Razor and CSS token sources without forcing rebuilds for ordinary C# changes.**
- [ ] **Step 4: Run UI tests, planning tests, and `dotnet build APS.slnx --no-restore`.**
- [ ] **Step 5: Commit `test: focus cockpit coverage and incremental styling build`.**

### Task 6: Preserve data, launch, and sync

**Files:**
- No production source changes.

**Interfaces:**
- Consumes: `%LOCALAPPDATA%\APS-Data\Data\aps.db`, desktop startup log, and current feature branch.
- Produces: verified development build and remote branch checkpoint; no release.

- [ ] **Step 1: Run `dotnet test APS.slnx --no-restore` and require zero failures.**
- [ ] **Step 2: Run `dotnet publish src/APS.DesktopHost/APS.DesktopHost.csproj -c Release -r win-x64 --self-contained true -o build/publish/workbench-0.4.0-cockpit`.**
- [ ] **Step 3: Run SQLite integrity and record-count checks before launch.**
- [ ] **Step 4: Stop only the verified prior APS executable and launch the cockpit build.**
- [ ] **Step 5: Verify startup, migration, and absence of startup errors from logs; repeat SQLite checks.**
- [ ] **Step 6: Push `agent/aps-dotnet-planning-core` and verify the remote branch points to the local commit. Do not tag or create a release.**
