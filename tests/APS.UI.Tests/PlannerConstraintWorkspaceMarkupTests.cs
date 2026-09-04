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
    public void What_if_analysis_renders_both_schedules_on_one_comparison_axis()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/PlanCompare.razor"));
        var visual = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Shared/ScenarioScheduleComparison.razor"));

        Assert.Contains("ScenarioScheduleComparison", source, StringComparison.Ordinal);
        Assert.Contains("Resource loading impact", source, StringComparison.Ordinal);
        Assert.Contains("Planning assumption changes", source, StringComparison.Ordinal);
        Assert.Contains("HorizonStartUtc=\"@CommonStart\"", visual, StringComparison.Ordinal);
        Assert.Contains("HorizonEndUtc=\"@CommonEnd\"", visual, StringComparison.Ordinal);
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
    public void Planning_preflight_is_read_only_and_routes_findings_to_the_right_editors()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/PlanningPreflight.razor"));

        Assert.Contains("@page \"/plan/preflight\"", source, StringComparison.Ordinal);
        Assert.Contains("IPlanningConfigurationDiagnosticsService", source, StringComparison.Ordinal);
        Assert.Contains("Preflight is intentionally non-destructive", source, StringComparison.Ordinal);
        Assert.Contains("finding.FixHref", source, StringComparison.Ordinal);
        Assert.Contains("READY FOR CALCULATE", source, StringComparison.Ordinal);
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
    public void Capability_calendar_combines_qualification_context_week_view_and_calendar_crud()
    {
        var page = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Pages/CapabilityCalendar.razor"));
        var calendar = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Shared/ResourceCapabilityCalendar.razor"));

        Assert.Contains("@page \"/plan/capability-calendar\"", page, StringComparison.Ordinal);
        Assert.Contains("ResourceCapabilityCalendar", page, StringComparison.Ordinal);
        Assert.Contains("Admin.CreateAsync(editing)", page, StringComparison.Ordinal);
        Assert.Contains("Admin.UpdateAsync(editing)", page, StringComparison.Ordinal);
        Assert.Contains("Admin.DeleteAsync<ResourceCalendar>", page, StringComparison.Ordinal);
        Assert.Contains("Qualified work", calendar, StringComparison.Ordinal);
        Assert.Contains("Add downtime", calendar, StringComparison.Ordinal);
        Assert.Contains("Add derating", calendar, StringComparison.Ordinal);
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
    public void Main_menu_exposes_planner_preflight_what_if_and_constraint_workspaces()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("Href=\"/plan/preflight\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/what-if\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/resource-constraints\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/capability-calendar\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/process-constraints\"", source, StringComparison.Ordinal);
    }
}
