namespace APS.UI.Tests;

public sealed class PlanningWorkbenchMarkupTests
{
    [Fact]
    public void Workbench_exposes_the_complete_planner_lifecycle()
    {
        var rail = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor"));

        foreach (var label in new[] { "Plan", "Campaigns", "Execution", "Recovery" })
            Assert.Contains(label, rail);

        Assert.Contains("Create recovery scenario", rail);
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
    public void Released_baseline_offers_a_real_working_scenario_transition()
    {
        var rail = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchLifecycleRail.razor"));

        Assert.Contains("Create planning scenario", rail);
        Assert.Contains("Create recovery scenario", rail);
    }

    [Fact]
    public void Dependency_layer_is_focused_on_the_selected_chain()
    {
        var gantt = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttTimelineViewport.razor"));
        var layer = File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/Gantt/GanttDependencyLayer.razor"));

        Assert.Contains("GanttDependencyLayer", gantt);
        Assert.Contains("State.SelectedPlanningKey", layer);
        Assert.DoesNotContain("@foreach (var edge in DependencyLines())", gantt);
    }

    [Fact]
    public void Planning_layers_are_explicit_and_binding_chain_is_truthfully_disabled_without_evidence()
    {
        var root = "src/APS.UI/Components/PlanningWorkbench/Gantt";
        foreach (var file in new[]
                 {
                     "GanttBaselineLayer.razor",
                     "GanttCalendarLayer.razor",
                     "GanttCampaignLayer.razor",
                     "GanttDependencyLayer.razor",
                     "GanttMarkerLayer.razor",
                     "GanttExecutionLayer.razor"
                 })
            Assert.True(File.Exists(Repo.File($"{root}/{file}")), $"Missing explicit planning layer: {file}");

        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));
        Assert.Contains("Binding chain unavailable", page);
        Assert.DoesNotContain("ShowCriticalPath", page);
        Assert.DoesNotContain("ToggleCriticalPath", page);
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
        Assert.Contains("<GanttProposalLayer", lane);
        Assert.Contains("string targetStartUtc", page);
    }

}
