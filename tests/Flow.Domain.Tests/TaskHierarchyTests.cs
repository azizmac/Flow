using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

/// <summary>Иерархия (docs/TZ_task_model.md §3): родитель в том же проекте и строго выше по уровню типа.</summary>
public class TaskHierarchyTests
{
    private static TaskType Type(Board board, TaskTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind);

    private static TaskItem Create(Board board, TaskTypeKind kind, string title = "T") =>
        board.CreateTask(title, typeId: Type(board, kind).Id);

    [Fact]
    public void SetParent_Accepts_Higher_Level_Parent_And_Clears()
    {
        var board = Board.Create("Проект", "PRJ");
        var epic = Create(board, TaskTypeKind.Epic);
        var story = Create(board, TaskTypeKind.Story);

        story.SetParent(epic, Type(board, TaskTypeKind.Story), Type(board, TaskTypeKind.Epic));
        Assert.Equal(epic.Id, story.ParentId);

        story.SetParent(null, Type(board, TaskTypeKind.Story), null);
        Assert.Null(story.ParentId);
    }

    [Theory]
    [InlineData(TaskTypeKind.Task, TaskTypeKind.Task)]
    [InlineData(TaskTypeKind.Subtask, TaskTypeKind.Task)]
    [InlineData(TaskTypeKind.Task, TaskTypeKind.Epic)]
    [InlineData(TaskTypeKind.Bug, TaskTypeKind.Task)]
    public void SetParent_Rejects_Parent_Not_Strictly_Higher(TaskTypeKind parentKind, TaskTypeKind childKind)
    {
        var board = Board.Create("Проект", "PRJ");
        var parent = Create(board, parentKind);
        var child = Create(board, childKind);

        Assert.Throws<InvalidOperationException>(() => child.SetParent(parent, Type(board, childKind), Type(board, parentKind)));
    }

    [Fact]
    public void SetParent_Rejects_Other_Project()
    {
        var board = Board.Create("Проект", "PRJ");
        var other = Board.Create("Другой", "OTH");
        var epic = Create(other, TaskTypeKind.Epic);
        var story = Create(board, TaskTypeKind.Story);

        Assert.Throws<InvalidOperationException>(() => story.SetParent(epic, Type(board, TaskTypeKind.Story), Type(other, TaskTypeKind.Epic)));
    }

    [Fact]
    public void SetParent_Rejects_Types_That_Are_Not_The_Tasks_Types()
    {
        var board = Board.Create("Проект", "PRJ");
        var epic = Create(board, TaskTypeKind.Epic);
        var story = Create(board, TaskTypeKind.Story);

        Assert.Throws<ArgumentException>(() => story.SetParent(epic, Type(board, TaskTypeKind.Task), Type(board, TaskTypeKind.Epic)));
        Assert.Throws<ArgumentException>(() => story.SetParent(epic, Type(board, TaskTypeKind.Story), Type(board, TaskTypeKind.Story)));
    }

    [Fact]
    public void ChangeType_Must_Stay_Between_Parent_And_Children()
    {
        var board = Board.Create("Проект", "PRJ");
        var story = Create(board, TaskTypeKind.Story);

        // Родитель — эпик (1), дети — подзадачи (4): допустимы уровни 2 и 3.
        story.ChangeType(Type(board, TaskTypeKind.Task), parentLevel: 1, minChildLevel: 4);
        Assert.Throws<InvalidOperationException>(() => story.ChangeType(Type(board, TaskTypeKind.Epic), parentLevel: 1, minChildLevel: 4));
        Assert.Throws<InvalidOperationException>(() => story.ChangeType(Type(board, TaskTypeKind.Subtask), parentLevel: 1, minChildLevel: 4));
    }

    [Fact]
    public void New_Task_Takes_Given_Rank_And_Rejects_Invalid()
    {
        var board = Board.Create("Проект", "PRJ");

        Assert.Equal("a5", board.CreateTask("A", rank: "a5").Rank);
        Assert.Throws<ArgumentException>(() => board.CreateTask("B", rank: "a50"));
    }
}
