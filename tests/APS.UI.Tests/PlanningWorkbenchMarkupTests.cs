namespace APS.UI.Tests;

public sealed class PlanningWorkbenchMarkupTests
{
    [Fact]
    public void Workbench_exposes_the_complete_planner_lifecycle()
    {
        var rail = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor"));
        var header = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor"));

        foreach (var label in new[] { "Plan", "Campaigns", "Execution", "Recovery" })
            Assert.Contains(label, rail);

        Assert.DoesNotContain("Create planning scenario", rail);
        Assert.DoesNotContain("Create recovery scenario", rail);
        Assert.Contains("Create planning scenario", header);
        Assert.Contains("Create recovery scenario", header);
        Assert.Contains("StartPlanning", header);
        Assert.Contains("StartRecovery", header);
    }

    [Fact]
    public void Lifecycle_rail_is_a_flat_native_radio_group()
    {
        var rail = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor"));

        foreach (var label in new[] { "Plan", "Campaigns", "Execution", "Recovery" })
            Assert.Contains(label, rail);

        Assert.Contains("<fieldset", rail);
        Assert.Contains("<legend class=\"sr-only\">Planning lifecycle</legend>", rail);
        Assert.Contains("type=\"radio\"", rail);
        Assert.Contains("name=\"planning-workbench-mode\"", rail);
        Assert.Contains("peer sr-only", rail);
        Assert.Contains("text-[13px]", rail);
        Assert.Contains("font-semibold", rail);
        Assert.Contains("peer-checked:after:h-0.5", rail);
        Assert.Contains("hover:text-primary", rail);
        Assert.Contains("peer-focus-visible:ring-2", rail);
        Assert.DoesNotContain("role=\"tab", rail);
        Assert.DoesNotContain("aria-selected", rail);
        Assert.DoesNotContain("bg-surface-inset", rail);
        Assert.DoesNotContain("shadow-inner", rail);
        Assert.DoesNotContain("grid-cols-4", rail);
        Assert.DoesNotContain("border-border-strong", rail);
        Assert.DoesNotContain("shadow-soft", rail);
    }

    [Fact]
    public void Lifecycle_radio_peer_state_is_scoped_to_each_mode_item()
    {
        var rail = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor"))
            .Replace("\r\n", "\n");

        var scopeStart = rail.IndexOf("<span class=\"relative\">", StringComparison.Ordinal);
        var input = scopeStart >= 0 ? rail.IndexOf("<input", scopeStart, StringComparison.Ordinal) : -1;
        var label = input >= 0 ? rail.IndexOf("<label", input, StringComparison.Ordinal) : -1;
        var scopeEnd = label >= 0 ? rail.IndexOf("</span>", label, StringComparison.Ordinal) : -1;

        Assert.True(scopeStart >= 0 && scopeStart < input && input < label && label < scopeEnd,
            "Each generated radio and its peer-styled label must share a neutral wrapper.");
    }

    [Fact]
    public void Scenario_header_preserves_identity_without_horizontal_scrolling()
    {
        var header = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor"));

        Assert.DoesNotContain("overflow-x-auto", header);
        Assert.Contains("w-56", header);
        Assert.Contains("shrink-0", header);
        Assert.DoesNotContain("Objective", header);
        Assert.DoesNotContain("Delivery reliability", header);
        Assert.Contains("Plan reference", header);
        Assert.Contains("PlanReferenceUtc", header);
        Assert.DoesNotContain(" UTC", header);
        Assert.Contains("dd MMM HH:mm'Z'", header);
    }

    [Fact]
    public void Scenario_header_uses_planner_language_and_primary_actions()
    {
        var header = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor"));

        foreach (var label in new[] { "Scenario", "Optimize", "Validate", "Release" })
            Assert.Contains(label, header);

        Assert.DoesNotContain(">Approve<", header);

        Assert.DoesNotContain("PlanVersionId", header);
    }

    [Fact]
    public void Workbench_has_one_consolidated_analysis_dock_below_the_schedule()
    {
        var dock = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchAnalysisDock.razor"));

        foreach (var label in new[] { "Overview", "Exceptions", "Capacity", "Delivery", "Material", "Compare", "Execution", "Traceability" })
            Assert.Contains(label, dock);
        Assert.Contains("PlannerAnalysisView", dock);
    }

    [Fact]
    public void Workbench_uses_the_lifecycle_as_its_only_content_navigation()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("QueueTitle", page);
        Assert.Contains("QueueDescription", page);
        Assert.DoesNotContain("Enum.GetValues<PlanningWorkbenchLens>()", page);
        Assert.DoesNotContain("Enum.GetValues<PlanningWorkbenchQueueContent>()", page);
        Assert.DoesNotContain("<WorkbenchCampaignRail", page);
        Assert.DoesNotContain("workbench.Queue.UnscheduledDemand", page);
    }

    [Fact]
    public void Execution_mode_surfaces_actual_status_and_timestamps()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var contract = File.ReadAllText(Repo.File("src/APS.Application/PlanningWorkbenchContracts.cs"));

        Assert.Contains("ExecutionStatus", page);
        Assert.Contains("ActualStartUtc", page);
        Assert.Contains("ActualEndUtc", page);
        Assert.Contains("ActualQuantityMt", contract);
        Assert.Contains("CanMoveSelectedOperation", page);
        Assert.Contains("OperationExecutionStatus.Completed", page);
        Assert.Contains("OperationExecutionStatus.Running", page);
    }

    [Fact]
    public void Inspector_exposes_baseline_scheduling_and_material_context_from_workbench_truth()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("Baseline change", page);
        Assert.Contains("SelectedBaseline", page);
        Assert.Contains("Scheduling basis", page);
        Assert.Contains("SelectedBindingEvidence", page);
        Assert.Contains("Material context", page);
        Assert.Contains("SelectedMaterialPools", page);
        Assert.Contains("SelectedMaterialReservations", page);
        Assert.DoesNotContain("material available assumed", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Released_baseline_offers_a_real_working_scenario_transition_from_the_scenario_action_area()
    {
        var header = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor"));

        Assert.Contains("CanStartRecovery", header);
        Assert.Contains("Mode is PlanningWorkbenchMode.Execution or PlanningWorkbenchMode.Recovery", header);
        Assert.Contains("Mode is PlanningWorkbenchMode.Plan or PlanningWorkbenchMode.Campaigns", header);
        Assert.Contains("StartPlanning.InvokeAsync", header);
        Assert.Contains("StartRecovery.InvokeAsync", header);
    }

    [Fact]
    public void Scenario_mode_selection_is_side_effect_free_and_recovery_is_labeled_as_a_draft()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("PlanReferenceUtc=\"@workbench.Plan.ReferenceTimeUtc\"", page);
        Assert.Contains("CanStartRecovery=\"@state.CanStartRecovery\"", page);
        Assert.Contains("PlanningScenarioIntent.Recovery => \"Draft\"", page);
        Assert.Contains("private void SetMode(PlanningWorkbenchMode mode)\n    {\n        state.SetMode(mode);\n    }", page.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Persistent_workbench_chrome_uses_readable_local_type_floors_instead_of_microscopic_text()
    {
        var grid = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceGrid.razor"));
        var scale = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimeScale.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));

        Assert.Contains("text-sm font-semibold leading-tight", grid);
        Assert.Contains("text-[13px] leading-tight", grid);
        Assert.Contains("aps-text-figure text-sm", grid);
        Assert.DoesNotContain("text-[9px]", grid);
        Assert.DoesNotContain("text-[10px]", grid);
        Assert.Contains("text-xs font-semibold", scale);
        Assert.Contains("text-xs", scale);
        Assert.DoesNotContain("text-[9px]", scale);
        Assert.Contains("text-xs", gantt);
    }

    [Fact]
    public void Gantt_status_context_reports_visible_operations_without_duplicate_mode_status()
    {
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));

        Assert.Contains("@State.VisibleStartUtc.ToString(\"dd MMM HH:mm\")", gantt);
        Assert.Contains("@State.VisibleEndUtc.ToString(\"dd MMM HH:mm\")", gantt);
        Assert.Contains("<span><strong>@Scene.VisibleOperationCount</strong> visible operations · <strong>@Scene.ResourceCount</strong> of <strong>@Scene.TotalResourceCount</strong> resources shown</span>", gantt);
        Assert.Contains("Process legend", gantt);
        Assert.DoesNotContain("<span><strong>@Scene.VisibleOperationCount</strong> mounted · <strong>@Scene.ResourceCount</strong> of <strong>@Scene.TotalResourceCount</strong> resources shown</span>", gantt);
        Assert.DoesNotContain("ModeLabel", gantt);
        Assert.DoesNotContain("<span><span class=\"text-muted\">Mode </span>", gantt);
    }

    [Fact]
    public void Dependency_rendering_is_focused_on_the_selected_chain()
    {
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimelineViewport.razor"));

        Assert.Contains("State.ShowDependencies && State.SelectedPlanningKey is not null", gantt);
        Assert.Contains("Scene.DependencyLines", gantt);
        Assert.Contains("edge.Type", gantt);
        Assert.Contains("edge.CurrentLagMinutes", gantt);
        Assert.Contains("edge.HeadroomMinutes", gantt);
        Assert.Contains("<title>", gantt);
        Assert.DoesNotContain("GanttDependencyLayer", gantt);
    }

    [Fact]
    public void Planning_layers_keep_their_behavior_without_render_only_component_boundaries()
    {
        var root = "src/APS.UI/Components/PlanningWorkbench/Gantt";
        foreach (var file in new[]
                 {
                     "GanttBaselineLayer.razor",
                     "GanttCalendarLayer.razor",
                     "GanttCampaignLayer.razor",
                     "GanttDependencyLayer.razor",
                     "GanttMarkerLayer.razor",
                     "GanttExecutionLayer.razor",
                     "GanttProposalLayer.razor"
                 })
            Assert.False(File.Exists(Repo.File($"{root}/{file}")), $"Render-only component should stay inlined: {file}");

        var lane = File.ReadAllText(Repo.File($"{root}/GanttResourceLane.razor"));
        var viewport = File.ReadAllText(Repo.File($"{root}/GanttTimelineViewport.razor"));
        Assert.Contains("State.ShowBaseline", lane);
        Assert.Contains("Row.CalendarIntervals", lane);
        Assert.Contains("Row.CampaignSpans", lane);
        Assert.Contains("ActualStartUtc", lane);
        Assert.Contains("State.ShowNowMarker", lane);
        Assert.Contains("State.StagedMove", lane);
        Assert.Contains("State.StagedBulkMove", lane);
        Assert.Contains("Scene.DependencyLines", viewport);

        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        Assert.Contains("Binding chain unavailable", page);
        Assert.DoesNotContain("ShowCriticalPath", page);
        Assert.DoesNotContain("ToggleCriticalPath", page);
    }

    [Fact]
    public void Capacity_panel_shares_the_gantt_viewport_and_exposes_resource_time_focus()
    {
        var panel = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttCapacityPanel.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));

        Assert.Contains("GanttCapacityModels.Build", panel);
        Assert.Contains("Processing", panel);
        Assert.Contains("Downtime", panel);
        Assert.Contains("Overload", panel);
        Assert.Contains("SegmentFocused", panel);
        Assert.Contains("GanttCapacityPanel", gantt);
        Assert.Contains("State.CapacityPanelOpen", gantt);
        Assert.Contains("FocusCapacity", File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor")));
        Assert.Contains("CapacityFocused", File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceLane.razor")));
    }

    [Fact]
    public void Baseline_compare_subrow_expands_shared_lane_geometry_and_keeps_current_blocks_distinct()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));
        var lane = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceLane.razor"));
        var block = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor"));

        Assert.Contains("CompareSubrow", page);
        Assert.Contains("State.GanttRowHeightPx", gantt);
        Assert.Contains("CompareSubrow", lane);
        Assert.Contains("VerticalClass", block);
    }

    [Fact]
    public void Gantt_uses_roving_keyboard_focus_and_a_keyboard_reachable_operation_menu()
    {
        var block = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));
        var menu = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationContextMenu.razor"));

        Assert.Contains("tabindex=\"@TabIndex\"", block);
        Assert.Contains("ShiftKey", block);
        Assert.Contains("GanttKeyboardDirection", block);
        Assert.Contains("KeyboardNavigate", gantt);
        Assert.Contains("KeyboardAnchorPlanningKey=\"@KeyboardAnchorPlanningKey\"", gantt);
        Assert.Contains("GanttOperationContextMenu", gantt);
        Assert.Contains("role=\"menu\"", menu);
        Assert.Contains("Inspect operation", menu);
        Assert.Contains("Show selected chain", menu);
        Assert.Contains("Fit operation", menu);
        Assert.Contains("Compare with baseline", menu);
        Assert.Contains("Move or reassign", menu);
        Assert.Contains("Find alternate resource", menu);
        Assert.Contains("Trace demand", menu);
        Assert.Contains("Trace campaign or heat", menu);
        Assert.Contains("Trace material", menu);
        Assert.Contains("Pin or unpin", menu);
        Assert.Contains("Repair selection", menu);
        Assert.Contains("Copy business ID", menu);
    }

    [Fact]
    public void Workbench_has_compact_fullscreen_chrome_and_a_synchronized_schedule_table()
    {
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));
        var list = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationList.razor"));
        var script = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/planning-workbench.js"));

        Assert.Contains("overflow-x-auto", page);
        Assert.DoesNotContain("flex flex-wrap items-center gap-2", page);
        Assert.Contains("ToggleFullscreenAsync", page);
        Assert.Contains("FullscreenChanged", page);
        Assert.Contains("toggleFullscreen", script);
        Assert.Contains("fullscreenchange", script);
        Assert.Contains("GanttOperationList", gantt);
        Assert.Contains("State.OperationListOpen", gantt);
        Assert.Contains("<table", list);
        Assert.Contains("Schedule operation list", list);
        Assert.Contains("OperationSelected", list);
    }

    [Fact]
    public void Gantt_is_a_reusable_synchronized_control_not_page_local_markup()
    {
        var root = "src/APS.UI/Components/PlanningWorkbench/Gantt";
        foreach (var file in new[]
                 {
                     "WorkbenchGantt.razor",
                     "GanttResourceGrid.razor",
                     "GanttTimeScale.razor",
                     "GanttTimelineViewport.razor",
                     "GanttResourceLane.razor",
                     "GanttOperationBlock.razor",
                     "GanttModels.cs"
                 })
            Assert.True(File.Exists(Repo.File($"{root}/{file}")), $"Missing reusable Gantt surface: {file}");

        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        Assert.Contains("<WorkbenchGantt", page);
        Assert.DoesNotContain("grid-cols-[176px_1fr]", page);
        Assert.DoesNotContain("<svg", page);
        Assert.DoesNotContain("aps-operation", page);
        Assert.DoesNotContain("Tight chain", page);
    }

    [Fact]
    public void Gantt_navigation_uses_frame_bounded_browser_geometry_and_meaningful_dotnet_transitions()
    {
        var script = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/planning-workbench.js"));
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/WorkbenchGantt.razor"));

        Assert.Contains("requestAnimationFrame", script);
        Assert.Contains("ResizeObserver", script);
        Assert.Contains("event.ctrlKey", script);
        Assert.Contains("PanViewport", script);
        Assert.Contains("SetVisibleRowRange", script);
        Assert.Contains("ApplyGanttPreferences", script);
        Assert.Contains("data-gantt-splitter", gantt);
        Assert.Contains("[JSInvokable]\n    public void ZoomAt", page.Replace("\r\n", "\n"));
        Assert.DoesNotContain("pointermove', () => dotnet", script);
    }

    [Fact]
    public void Drag_uses_a_readonly_source_and_an_eligibility_aware_proposal_ghost()
    {
        var script = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/planning-workbench.js"));
        var block = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor"));
        var lane = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceLane.razor"));
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("cloneNode(true)", script);
        Assert.Contains("aps-operation-ghost", script);
        Assert.Contains("aps-operation-source-dragging", script);
        Assert.Contains("eligibleResources", script);
        Assert.Contains("pointercancel', cancel", script);
        Assert.Contains("window.addEventListener('blur', cancel)", script);
        Assert.Contains("snapped.iso", script);
        Assert.DoesNotContain("state.drag.block.style", script);
        Assert.Contains("data-eligible-resources", block);
        Assert.Contains("data-drag-protected", block);
        Assert.Contains("State.StagedMove", lane);
        Assert.Contains("State.StagedBulkMove", lane);
        Assert.DoesNotContain("GanttProposalLayer", lane);
        Assert.Contains("string targetStartUtc", page);
    }

    [Fact]
    public void Shift_snap_uses_the_target_resources_authoritative_calendar_boundaries()
    {
        var script = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/planning-workbench.js"));
        var block = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttOperationBlock.razor"));
        var lane = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttResourceLane.razor"));
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("data-shift-boundaries=\"@ShiftBoundaries\"", lane);
        Assert.DoesNotContain("data-shift-boundaries", block);
        Assert.Contains("grid.dataset.shiftBoundaries", script);
        Assert.Contains("TargetShiftBoundaries(targetResourceId)", page);
        Assert.Contains("Shift unavailable", page);
        Assert.Contains("HasShiftBoundaries", page);
        Assert.Contains("targetShiftBoundaries.Count == 0", page);
        Assert.Contains("Shift calendar unavailable for resource", script);
        Assert.Contains("snapped.unavailable", script);
        Assert.Contains("state.Viewport.SnapMode,\n            targetShiftBoundaries", page.Replace("\r\n", "\n"));
    }
}
