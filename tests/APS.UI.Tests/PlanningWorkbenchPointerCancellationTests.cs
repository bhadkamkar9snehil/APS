namespace APS.UI.Tests;

public sealed class PlanningWorkbenchPointerCancellationTests
{
    [Fact]
    public void Pointer_cancel_rolls_back_every_active_gesture_without_committing_it()
    {
        var script = File.ReadAllText(Repo.File("src/APS.UI/wwwroot/planning-workbench.js"));
        const string startMarker = "const cancel = () => {";
        const string endMarker = "const keydown = event => {";
        var start = script.IndexOf(startMarker, StringComparison.Ordinal);
        var end = script.IndexOf(endMarker, start, StringComparison.Ordinal);

        Assert.True(start >= 0, "The planning workbench must define one central gesture-cancellation handler.");
        Assert.True(end > start, "The cancellation handler must end before keyboard gesture handling begins.");
        var cancel = script[start..end];

        foreach (var state in new[]
                 {
                     "state.capacitySplit = null;",
                     "state.columnSplit = null;",
                     "state.split = null;",
                     "state.pan = null;",
                     "state.drag = null;"
                 })
            Assert.Contains(state, cancel);

        Assert.Contains("cleanupDrag(drag)", cancel);
        Assert.Contains("document.body.style.cursor = ''", cancel);
        Assert.DoesNotContain("invokeMethodAsync", cancel);
        Assert.DoesNotContain("StageDraggedMove", cancel);
        Assert.DoesNotContain("StageDraggedBulkMove", cancel);
        Assert.DoesNotContain("SetGridWidth", cancel);
        Assert.DoesNotContain("SetGridColumnWidth", cancel);
        Assert.DoesNotContain("SetCapacityPanelHeight", cancel);
        Assert.DoesNotContain("PanViewport", cancel);

        Assert.Contains("root.addEventListener('pointercancel', cancel);", script);
        Assert.Contains("window.addEventListener('blur', cancel);", script);
        Assert.Contains("root.removeEventListener('pointercancel', cancel);", script);
        Assert.Contains("window.removeEventListener('blur', cancel);", script);
    }
}
