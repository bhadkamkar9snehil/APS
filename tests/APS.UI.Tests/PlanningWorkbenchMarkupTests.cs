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
        var page = File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/FiniteSchedule.razor"));

        Assert.Contains("FocusedDependencyLines", page);
        Assert.Contains("state.SelectedPlanningKey", page);
        Assert.DoesNotContain("@foreach (var edge in DependencyLines())", page);
    }

}
