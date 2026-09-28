using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Экраны задач и обязательные поля перехода (docs/TZ_workflow_config.md §2–3, этап 3C).</summary>
public class TaskScreenTests
{
    [Fact]
    public void Screen_Accepts_Known_Fields_Once_And_Only_Create_Fields_On_Create()
    {
        var board = Board.Create("Проект", "PRJ");
        var sla = board.AddCustomField("sla", "SLA", CustomFieldType.Text);

        board.SetScreen(null, ScreenContext.Detail, [new("system:due"), new($"custom:{sla.Id}", true, "Поддержка")]);

        Assert.Throws<InvalidOperationException>(() => board.SetScreen(null, ScreenContext.Create, [new("system:due")]));
        Assert.Throws<InvalidOperationException>(() => board.SetScreen(null, ScreenContext.Detail, [new("system:due"), new("system:due")]));
        Assert.Throws<InvalidOperationException>(() => board.SetScreen(null, ScreenContext.Detail, [new("system:title")]));
        Assert.Throws<InvalidOperationException>(() => board.SetScreen(null, ScreenContext.Detail, [new($"custom:{Guid.NewGuid()}")]));
        Assert.Throws<InvalidOperationException>(() => board.SetScreen(Guid.NewGuid(), ScreenContext.Detail, []));
        Assert.Throws<ArgumentException>(() => new ScreenField("system:due", section: new string('x', 41)));
    }

    [Fact]
    public void Resolve_Prefers_Type_Then_All_Types_Then_Builtin()
    {
        var board = Board.Create("Проект", "PRJ");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug).Id;
        var task = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Task).Id;

        Assert.Equal(ScreenFields.System.Count, board.ResolveScreen(bug, ScreenContext.Detail).Count);
        Assert.Equal(["system:type", "system:priority", "system:assignee", "system:description"],
            board.ResolveScreen(bug, ScreenContext.Create).Select(f => f.Field));

        board.SetScreen(null, ScreenContext.Detail, [new("system:due")]);
        board.SetScreen(bug, ScreenContext.Detail, [new("system:priority")]);

        Assert.Equal("system:priority", Assert.Single(board.ResolveScreen(bug, ScreenContext.Detail)).Field);
        Assert.Equal("system:due", Assert.Single(board.ResolveScreen(task, ScreenContext.Detail)).Field);

        board.ResetScreen(bug, ScreenContext.Detail);
        Assert.Equal("system:due", Assert.Single(board.ResolveScreen(bug, ScreenContext.Detail)).Field);
    }

    [Fact]
    public void Create_Screen_Required_Fields_Are_Reported()
    {
        var board = Board.Create("Проект", "PRJ");
        board.SetScreen(null, ScreenContext.Create, [new("system:assignee", true), new("system:description", true), new("system:priority")]);
        var task = board.CreateTask("Задача");

        Assert.Equal(["Исполнитель", "Описание"], board.MissingOnCreateScreen(task));

        task.Assign(Guid.NewGuid());
        task.UpdateDescription("есть");
        Assert.Empty(board.MissingOnCreateScreen(task));
    }

    [Fact]
    public void Transition_Requires_Filled_Custom_Fields()
    {
        var board = Board.Create("Проект", "PRJ");
        var steps = board.AddCustomField("steps", "Шаги", CustomFieldType.Text);
        var from = board.Statuses.Single(s => s.IsInitial).Id;
        var to = board.Statuses.Single(s => s.IsFinal).Id;
        board.SetWorkflow(WorkflowMode.Restricted, [
            new(null, to, Conditions: new TransitionConditions(RequireFields: [steps.Id])),
            new(to, from)
        ]);

        var denied = board.CheckTransition(from, to, new TransitionContext(ProjectRole.Admin, true, true, true, new HashSet<Guid>()));
        Assert.Equal(["Заполните поле «Шаги»"], denied.Reasons);
        Assert.True(board.CheckTransition(from, to, new TransitionContext(ProjectRole.Admin, true, true, true, new HashSet<Guid> { steps.Id })).Allowed);
        Assert.Throws<InvalidOperationException>(() => board.SetWorkflow(WorkflowMode.Free,
            [new(null, to, Conditions: new TransitionConditions(RequireFields: [Guid.NewGuid()]))]));
    }
}
