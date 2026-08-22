using APS.Application;
using APS.Domain;
using APS.UI.Components.PlanningWorkbench.Gantt;
using APS.UI.State;
using Bunit;
using Microsoft.AspNetCore.Components.Web;

namespace APS.UI.Tests;

public sealed class GanttOperationBlockComponentTests : BunitContext
{
    private static readonly DateTime Start = new(2026, 8, 22, 6, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(OperationExecutionStatus.Running)]
    [InlineData(OperationExecutionStatus.Completed)]
    public void Running_and_completed_operations_are_drag_protected_even_when_editing_is_enabled(
        OperationExecutionStatus executionStatus)
    {
        var model = Model(executionStatus);

        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.CanEdit, true));

        Assert.Equal("true", cut.Find("button").GetAttribute("data-drag-protected"));
    }

    [Fact]
    public void Rendered_operation_retains_current_and_alternate_resource_ids_for_drag_eligibility()
    {
        var currentResourceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var alternateResourceId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var model = Model(
            OperationExecutionStatus.Planned,
            currentResourceId,
            [
                new PlanningOperationResourceOptionView(currentResourceId, "LRF-01", "Ladle furnace 01", 55, 0, true, "ROUTE"),
                new PlanningOperationResourceOptionView(alternateResourceId, "LRF-02", "Ladle furnace 02", 55, 5, false, "ROUTE")
            ]);

        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.CanEdit, true));

        var eligible = cut.Find("button").GetAttribute("data-eligible-resources")!
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(Guid.Parse)
            .ToHashSet();

        Assert.Equal(2, eligible.Count);
        Assert.Contains(currentResourceId, eligible);
        Assert.Contains(alternateResourceId, eligible);
    }

    [Fact]
    public void Ctrl_click_emits_toggle_selection_for_the_rendered_operation()
    {
        var model = Model(OperationExecutionStatus.Planned);
        GanttOperationSelectionRequest? request = null;
        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.CanEdit, true)
            .Add(component => component.OperationSelected, value => request = value));

        cut.Find("button").Click(new MouseEventArgs { CtrlKey = true });

        Assert.NotNull(request);
        Assert.Equal(model.Operation.PlanningKey, request!.Operation.PlanningKey);
        Assert.True(request.Toggle);
        Assert.False(request.Extend);
    }

    [Fact]
    public void Shift_click_emits_range_extension_selection()
    {
        var model = Model(OperationExecutionStatus.Planned);
        GanttOperationSelectionRequest? request = null;
        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.OperationSelected, value => request = value));

        cut.Find("button").Click(new MouseEventArgs { ShiftKey = true });

        Assert.NotNull(request);
        Assert.False(request!.Toggle);
        Assert.True(request.Extend);
    }

    [Fact]
    public void Shift_F10_opens_the_operation_context_menu_from_keyboard()
    {
        var model = Model(OperationExecutionStatus.Planned);
        GanttContextMenuRequest? request = null;
        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.ContextRequested, value => request = value));

        cut.Find("button").KeyDown(new KeyboardEventArgs { Key = "F10", ShiftKey = true });

        Assert.NotNull(request);
        Assert.Equal(model.Operation.PlanningKey, request!.PlanningKey);
        Assert.True(request.FromKeyboard);
        Assert.Equal(0d, request.ClientX);
        Assert.Equal(0d, request.ClientY);
    }

    [Fact]
    public void Arrow_key_emits_Gantt_keyboard_navigation_without_mutating_selection_locally()
    {
        var model = Model(OperationExecutionStatus.Planned);
        GanttKeyboardNavigationRequest? request = null;
        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.KeyboardNavigate, value => request = value));

        cut.Find("button").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        Assert.NotNull(request);
        Assert.Equal(model.Operation.PlanningKey, request!.PlanningKey);
        Assert.Equal(GanttKeyboardDirection.Right, request.Direction);
    }

    [Fact]
    public void Alt_arrow_is_reserved_for_viewport_panning_and_is_not_emitted_as_operation_navigation()
    {
        var model = Model(OperationExecutionStatus.Planned);
        var emitted = false;
        var cut = Render<GanttOperationBlock>(parameters => parameters
            .Add(component => component.Model, model)
            .Add(component => component.State, State())
            .Add(component => component.KeyboardNavigate, _ => emitted = true));

        cut.Find("button").KeyDown(new KeyboardEventArgs { Key = "ArrowRight", AltKey = true });

        Assert.False(emitted);
    }

    private static PlanningWorkbenchState State()
    {
        var state = new PlanningWorkbenchState();
        state.SetPlanWindow(Start, Start.AddDays(2), Start, Start.AddDays(1));
        return state;
    }

    private static GanttOperationModel Model(
        OperationExecutionStatus executionStatus,
        Guid? resourceId = null,
        IReadOnlyList<PlanningOperationResourceOptionView>? resourceOptions = null)
    {
        var actualResourceId = resourceId ?? Guid.Parse("11111111-1111-1111-1111-111111111111");
        var operation = new ScheduledProcessOperationView(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "HEAT-2042:LRF",
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            ProcessOperationType.Lrf,
            actualResourceId,
            "LRF-01",
            "Ladle furnace 01",
            ProcessUnitType.Lrf,
            ResourceOperatingState.Available,
            Start.AddHours(2),
            Start.AddHours(2).AddMinutes(55),
            90m,
            "G42",
            "BLT-150");
        var options = resourceOptions ??
        [
            new PlanningOperationResourceOptionView(actualResourceId, "LRF-01", "Ladle furnace 01", 55, 0, true, "ROUTE")
        ];
        var detail = new PlanningOperationWorkbenchDetail(
            operation.OperationSnapshotId,
            operation.PlanningKey,
            operation.SourceEntityId,
            OperationAssignmentCommitmentState.Flexible,
            executionStatus,
            executionStatus == OperationExecutionStatus.Running ? operation.StartUtc.AddMinutes(5) : null,
            executionStatus == OperationExecutionStatus.Completed ? operation.EndUtc : null,
            executionStatus == OperationExecutionStatus.Completed ? operation.QuantityMt : 0m,
            Array.Empty<string>(),
            options,
            "CMP-G42-01",
            7,
            ["PO-10042"]);

        return new GanttOperationModel(
            operation,
            detail,
            executionStatus,
            12d,
            18d,
            180d,
            "HEAT-2042 LRF",
            "LRF",
            "Heat 2042 ladle refining",
            options.Count == 1,
            executionStatus == OperationExecutionStatus.Running ? 35d : 0d,
            GanttBaselineChange.Unchanged)
        {
            CommitmentState = OperationAssignmentCommitmentState.Flexible,
            EligibleResourceCount = options.Count,
            ActualStartUtc = detail.ActualStartUtc,
            ActualEndUtc = detail.ActualEndUtc
        };
    }
}
