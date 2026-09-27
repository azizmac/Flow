using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Слияние, разделение, перенос в домене (docs/TZ_task_model.md §6): приём задачи другим проектом, перенос комментария и вложения, алиас.</summary>
public class TaskRestructureTests
{
    [Fact]
    public void ReceiveTask_Gives_A_New_Code_And_Resets_Sprint_And_Milestone()
    {
        var source = Board.Create("Источник", "SRC");
        var target = Board.Create("Цель", "DST");
        target.CreateTask("Уже есть");
        var parent = source.CreateTask("Родитель", typeId: source.TaskTypes.First(t => t.Kind == TaskTypeKind.Story).Id);
        var task = source.CreateTask("Задача");
        task.SetSprint(Sprint.Create(source.Id, "Спринт", null, 0));
        task.SetMilestone(Milestone.Create(source.Id, "1.0", null, null, 0));
        task.SetParent(parent, source.GetTaskType(task.TypeId), source.GetTaskType(parent.TypeId));

        var status = target.Statuses.Single(s => s.IsInitial).Id;
        var type = target.TaskTypes.Single(t => t.IsDefault).Id;
        var old = target.ReceiveTask(task, status, type, "a1", "{}", keepParent: false);

        Assert.Equal("SRC-2", old.Value);
        Assert.Equal(("DST-2", target.Id, status, type), (task.Code.Value, task.BoardId, task.StatusId, task.TypeId));
        Assert.Equal((null, null, null), (task.SprintId, task.MilestoneId, task.ParentId));
        Assert.Equal(2, target.NextTaskNumber);
    }

    [Fact]
    public void ReceiveTask_Rejects_Its_Own_Task_Foreign_Status_And_Archived_Type()
    {
        var source = Board.Create("Источник", "SRC");
        var target = Board.Create("Цель", "DST");
        var task = source.CreateTask("Задача");
        var status = target.Statuses.Single(s => s.IsInitial).Id;
        var type = target.TaskTypes.Single(t => t.IsDefault).Id;
        var archived = target.TaskTypes.First(t => !t.IsDefault);
        target.SetTaskTypeArchived(archived.Id, true);

        Assert.Throws<InvalidOperationException>(() => source.ReceiveTask(task, source.Statuses.First().Id, task.TypeId, "a1", "{}", false));
        Assert.Throws<InvalidOperationException>(() => target.ReceiveTask(task, source.Statuses.First().Id, type, "a1", "{}", false));
        Assert.Throws<InvalidOperationException>(() => target.ReceiveTask(task, status, archived.Id, "a1", "{}", false));
        Assert.Equal(0, target.NextTaskNumber);
    }

    [Fact]
    public void Comment_Transfer_Marks_The_Origin_And_Keeps_The_Limit()
    {
        var comment = TaskComment.Create(Guid.NewGuid(), Guid.NewGuid(), new string('x', TaskComment.BodyMaxLength), []);
        var target = Guid.NewGuid();

        comment.TransferTo(target, "PROJ-12");

        Assert.Equal(target, comment.TaskId);
        Assert.StartsWith("_из PROJ-12_\n\n", comment.Body);
        Assert.Equal(TaskComment.BodyMaxLength, comment.Body.Length);
    }

    [Fact]
    public void Attachment_Relocate_Builds_A_Key_Under_The_New_Task_And_Project()
    {
        var attachment = Attachment.Create(Guid.NewGuid(), Guid.NewGuid(), "отчёт.pdf", "application/pdf", 10, new byte[32], Guid.NewGuid());
        var task = Guid.NewGuid();
        var board = Guid.NewGuid();

        var old = attachment.Relocate(task, board);

        Assert.NotEqual(old, attachment.StorageKey);
        Assert.StartsWith(Attachment.TaskPrefix(board, task), attachment.StorageKey);
        Assert.EndsWith(".pdf", attachment.StorageKey);
    }

    [Fact]
    public void Custom_Fields_Are_Copied_Only_Within_A_Project()
    {
        var board = Board.Create("Проект", "PRJ");
        var source = board.CreateTask("Образец");
        var copy = board.CreateTask("Часть");
        var foreign = Board.Create("Другой", "OTH").CreateTask("Чужая");

        copy.CopyCustomFieldsFrom(source);
        Assert.Throws<InvalidOperationException>(() => foreign.CopyCustomFieldsFrom(source));
    }

    [Fact]
    public void Alias_Keeps_The_Code_And_Can_Be_Repointed()
    {
        var alias = TaskCodeAlias.Create(TaskCode.Create("SRC", 7), Guid.NewGuid());
        var other = Guid.NewGuid();

        alias.Repoint(other);

        Assert.Equal(("SRC-7", other), (alias.Code, alias.TaskId));
    }
}
