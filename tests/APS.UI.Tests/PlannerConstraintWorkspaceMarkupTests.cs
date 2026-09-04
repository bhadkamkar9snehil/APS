namespace APS.UI.Tests;

public sealed class PlannerConstraintWorkspaceMarkupTests
{
    [Fact]
    public void What_if_workspace_creates_an_explicit_child_and_opens_compare()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/PlanningWhatIf.razor"));

        Assert.Contains("@page \"/plan/what-if\"", source, StringComparison.Ordinal);
        Assert.Contains("UseBaselinePlanningControls: false", source, StringComparison.Ordinal);
        Assert.Contains("WorkspaceState.SetPlan(outcome.Version.PlanVersionId, baseline.PlanVersionId)", source, StringComparison.Ordinal);
        Assert.Contains("Navigation.NavigateTo(\"/decide/compare\")", source, StringComparison.Ordinal);
        Assert.Contains("does not approve or release the child", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_version_replan_preserves_selected_baseline_horizon_and_repair_scope()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/PlanVersions.razor"));

        Assert.Contains("BuildCalculationRequest(baseline.HorizonStartUtc, baseline.HorizonEndUtc)", source, StringComparison.Ordinal);
        Assert.Contains("RepairScope: Constraints.BuildRepairScopePolicy()", source, StringComparison.Ordinal);
        Assert.Contains("UseBaselinePlanningControls: false", source, StringComparison.Ordinal);
        Assert.Contains("preserve horizon", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resource_constraint_workspace_edits_authoritative_eligibility_and_calendars()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/ResourceConstraints.razor"));

        Assert.Contains("@page \"/plan/resource-constraints\"", source, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editingCapability)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.UpdateAsync(editingCapability)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<RouteResourceCapability>", source, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editingCalendar)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.UpdateAsync(editingCalendar)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<ResourceCalendar>", source, StringComparison.Ordinal);
        Assert.Contains("Confirm delete", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Process_constraint_workspace_edits_sequence_and_thermal_constraints()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/ProcessConstraints.razor"));

        Assert.Contains("@page \"/plan/process-constraints\"", source, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editingTransition)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<TransitionRule>", source, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editingGradeTemperature)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<GradeProcessTemperatureRequirement>", source, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editingResourceTemperature)", source, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<ResourceTemperatureCapability>", source, StringComparison.Ordinal);
        Assert.Contains("Confirm delete", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_menu_exposes_planner_what_if_and_constraint_workspaces()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("Href=\"/plan/what-if\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/resource-constraints\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/process-constraints\"", source, StringComparison.Ordinal);
    }
}
