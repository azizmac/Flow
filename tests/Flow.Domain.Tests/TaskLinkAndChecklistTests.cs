using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Связи (docs/TZ_task_model.md §5) и чек-лист (§8): инварианты сущностей.</summary>
public class TaskLinkAndChecklistTests
{
    [Fact]
    public void Link_Rejects_Self_And_Unknown_Type()
    {
        var id = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => TaskLink.Create(id, id, TaskLinkType.Blocks, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => TaskLink.Create(id, Guid.NewGuid(), (TaskLinkType)42, Guid.NewGuid()));
    }

    [Fact]
    public void RelatesTo_Is_Stored_In_Canonical_Order_Others_Keep_Direction()
    {
        var (a, b) = (Guid.NewGuid(), Guid.NewGuid());
        var (low, high) = a.CompareTo(b) < 0 ? (a, b) : (b, a);

        var forward = TaskLink.Create(high, low, TaskLinkType.RelatesTo, Guid.NewGuid());
        Assert.Equal((low, high), (forward.SourceTaskId, forward.TargetTaskId));

        var blocks = TaskLink.Create(high, low, TaskLinkType.Blocks, Guid.NewGuid());
        Assert.Equal((high, low), (blocks.SourceTaskId, blocks.TargetTaskId));
        Assert.True(blocks.IsOutwardFor(high));
        Assert.Equal(high, blocks.OtherTaskId(low));
    }

    [Fact]
    public void Activity_Marks_Inward_Side_Except_For_Symmetric_Link()
    {
        var task = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.Equal("Blocks:in", TaskActivity.LinkAdded(task, Guid.NewGuid(), TaskLinkType.Blocks, false, other).OldValue);
        Assert.Equal("Blocks", TaskActivity.LinkAdded(task, Guid.NewGuid(), TaskLinkType.Blocks, true, other).OldValue);
        Assert.Equal("RelatesTo", TaskActivity.LinkRemoved(task, Guid.NewGuid(), TaskLinkType.RelatesTo, false, other).OldValue);
    }

    private static TaskItem NewTask() => Board.Create("Проект", "PRJ").CreateTask("Задача");

    [Fact]
    public void Checklist_Add_Edit_Toggle_Remove()
    {
        var task = NewTask();
        var actor = Guid.NewGuid();
        var first = task.AddChecklistItem("  Макет  ");
        var second = task.AddChecklistItem("Вёрстка");

        Assert.Equal("Макет", first.Text);
        Assert.Equal([first.Id, second.Id], task.Checklist.Select(i => i.Id));

        Assert.False(task.EditChecklistItem(first.Id, "Макет"));
        Assert.True(task.EditChecklistItem(first.Id, "Макет v2"));

        Assert.True(task.SetChecklistItemDone(second.Id, true, actor));
        Assert.False(task.SetChecklistItemDone(second.Id, true, actor));
        Assert.Equal((1, 2), (task.ChecklistDone, task.ChecklistTotal));
        Assert.Equal(actor, second.DoneById);
        Assert.NotNull(second.DoneAt);

        task.SetChecklistItemDone(second.Id, false, actor);
        Assert.Null(second.DoneById);

        task.RemoveChecklistItem(first.Id);
        Assert.Equal(1, task.ChecklistTotal);
        Assert.Throws<InvalidOperationException>(() => task.RemoveChecklistItem(first.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Checklist_Rejects_Empty_Text(string text)
    {
        Assert.Throws<ArgumentException>(() => NewTask().AddChecklistItem(text));
    }

    [Fact]
    public void Checklist_Rejects_Too_Long_Text_And_Too_Many_Items()
    {
        var task = NewTask();
        Assert.Throws<ArgumentException>(() => task.AddChecklistItem(new string('x', TaskChecklistItem.TextMaxLength + 1)));

        for (var i = 0; i < TaskItem.MaxChecklistItems; i++)
            task.AddChecklistItem($"Пункт {i}");
        Assert.Throws<InvalidOperationException>(() => task.AddChecklistItem("Лишний"));
    }

    [Fact]
    public void Checklist_Reorder_Requires_Full_Permutation()
    {
        var task = NewTask();
        var a = task.AddChecklistItem("A").Id;
        var b = task.AddChecklistItem("B").Id;
        var c = task.AddChecklistItem("C").Id;

        task.ReorderChecklist([c, a, b]);
        Assert.Equal([c, a, b], task.Checklist.Select(i => i.Id));

        Assert.Throws<ArgumentException>(() => task.ReorderChecklist([c, a]));
        Assert.Throws<ArgumentException>(() => task.ReorderChecklist([c, a, a]));

        // Новый пункт — в конец и после перестановки.
        var d = task.AddChecklistItem("D").Id;
        Assert.Equal(d, task.Checklist[^1].Id);
    }
}
