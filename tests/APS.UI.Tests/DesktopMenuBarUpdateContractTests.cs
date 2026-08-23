namespace APS.UI.Tests;

public sealed class DesktopMenuBarUpdateContractTests
{
    [Fact]
    public void Menu_bar_owns_update_status_subscription_and_cleanup()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("@implements IDisposable", razor);
        Assert.Contains("Updates.Changed += OnUpdatesChanged", razor);
        Assert.Contains("InvokeAsync(StateHasChanged)", razor);
        Assert.Contains("Updates.Changed -= OnUpdatesChanged", razor);
    }

    [Fact]
    public void Help_menu_preserves_complete_update_lifecycle()
    {
        var razor = File.ReadAllText(Repo.File("src/APS.UI/Components/Layout/DesktopMenuBar.razor"));

        Assert.Contains("UpdatePhase.Available", razor);
        Assert.Contains("Updates.DownloadAsync()", razor);
        Assert.Contains("UpdatePhase.Downloading", razor);
        Assert.Contains("DownloadProgress", razor);
        Assert.Contains("UpdatePhase.ReadyToRestart", razor);
        Assert.Contains("Updates.RestartAndApply", razor);
        Assert.Contains("UpdatePhase.Failed", razor);
        Assert.Contains("FailureCode", razor);
    }
}
