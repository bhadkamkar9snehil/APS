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
    public void Main_menu_exposes_what_if_and_resource_constraint_workspaces()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("Href=\"/plan/what-if\"", source, StringComparison.Ordinal);
        Assert.Contains("Href=\"/plan/resource-constraints\"", source, StringComparison.Ordinal);
    }
}
