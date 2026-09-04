namespace APS.UI.Tests;

public sealed class WorkbenchReleaseGateMarkupTests
{
    [Fact]
    public void Workbench_requires_approval_before_showing_release_action()
    {
        var source = System.IO.File.ReadAllText(Repo.File("src/APS.UI/Components/PlanningWorkbench/WorkbenchScenarioHeader.razor"));

        Assert.Contains("!IsReleased && IsApproved", source, StringComparison.Ordinal);
        Assert.Contains("Review &amp; approve", source, StringComparison.Ordinal);
        Assert.Contains("href=\"/plan/versions\"", source, StringComparison.Ordinal);
        Assert.Contains("PlanVersionStatus.Approved", source, StringComparison.Ordinal);
    }
}
