using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class TaskTypeTests
{
    [Fact]
    public void Create_Should_SeedDefaultTaskTypes_WithSingleDefault()
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Equal(DefaultTaskTypes.All.Select(t => t.Name), board.TaskTypes.OrderBy(t => t.SortOrder).Select(t => t.Name));
        var @default = Assert.Single(board.TaskTypes, t => t.IsDefault);
        Assert.Equal(TaskTypeKind.Task, @default.Kind);
        Assert.All(board.TaskTypes, t => Assert.Equal(board.Id, t.BoardId));
    }

    [Theory]
    [InlineData(TaskTypeKind.Epic, 1)]
    [InlineData(TaskTypeKind.Story, 2)]
    [InlineData(TaskTypeKind.Task, 3)]
    [InlineData(TaskTypeKind.Bug, 3)]
    [InlineData(TaskTypeKind.Subtask, 4)]
    public void Level_Should_FollowKind(TaskTypeKind kind, int level) => Assert.Equal(level, kind.Level());

    [Fact]
    public void AddTaskType_Should_AppendWithNextSortOrder()
    {
        var board = Board.Create("Flow", "FLW");
        var maxOrder = board.TaskTypes.Max(t => t.SortOrder);

        var type = board.AddTaskType("  Улучшение  ", TaskTypeKind.Task);

        Assert.Equal("Улучшение", type.Name);
        Assert.Equal(maxOrder + 1, type.SortOrder);
        Assert.False(type.IsDefault);
        Assert.Equal(3, type.Level);
    }

    [Fact]
    public void AddTaskType_Should_MoveDefaultFlag_When_IsDefault()
    {
        var board = Board.Create("Flow", "FLW");

        var type = board.AddTaskType("Инцидент", TaskTypeKind.Bug, isDefault: true);

        Assert.Equal(type.Id, Assert.Single(board.TaskTypes, t => t.IsDefault).Id);
    }

    [Fact]
    public void AddTaskType_Should_Throw_When_NameTakenIgnoringCase()
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Throws<InvalidOperationException>(() => board.AddTaskType("ошибка", TaskTypeKind.Bug));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTaskType_Should_Throw_When_NameEmpty(string name)
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Throws<ArgumentException>(() => board.AddTaskType(name, TaskTypeKind.Task));
    }

    [Fact]
    public void AddTaskType_Should_Throw_When_NameTooLong()
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Throws<ArgumentException>(() => board.AddTaskType(new string('a', TaskType.NameMaxLength + 1), TaskTypeKind.Task));
    }

    [Fact]
    public void AddTaskType_Should_Throw_When_KindUnknown()
    {
        var board = Board.Create("Flow", "FLW");

        Assert.Throws<ArgumentException>(() => board.AddTaskType("Странный", (TaskTypeKind)42));
    }

    [Fact]
    public void RenameTaskType_Should_AllowSameNameOnSameType_AndRejectOtherTypesName()
    {
        var board = Board.Create("Flow", "FLW");
        var story = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Story);

        board.RenameTaskType(story.Id, "ИСТОРИЯ");
        Assert.Equal("ИСТОРИЯ", story.Name);

        Assert.Throws<InvalidOperationException>(() => board.RenameTaskType(story.Id, "Эпик"));
    }

    [Fact]
    public void RenameTaskType_Should_Throw_When_TypeFromOtherBoard()
    {
        var board = Board.Create("Flow", "FLW");
        var other = Board.Create("Other", "OTH");

        Assert.Throws<InvalidOperationException>(() => board.RenameTaskType(other.TaskTypes.First().Id, "X"));
    }

    [Fact]
    public void SetDefaultTaskType_Should_KeepExactlyOneDefault()
    {
        var board = Board.Create("Flow", "FLW");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);

        board.SetDefaultTaskType(bug.Id);

        Assert.Equal(bug.Id, Assert.Single(board.TaskTypes, t => t.IsDefault).Id);
    }

    [Fact]
    public void SetDefaultTaskType_Should_Throw_When_Archived()
    {
        var board = Board.Create("Flow", "FLW");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        board.SetTaskTypeArchived(bug.Id, true);

        Assert.Throws<InvalidOperationException>(() => board.SetDefaultTaskType(bug.Id));
    }

    [Fact]
    public void SetTaskTypeArchived_Should_Throw_When_Default()
    {
        var board = Board.Create("Flow", "FLW");
        var @default = board.TaskTypes.Single(t => t.IsDefault);

        Assert.Throws<InvalidOperationException>(() => board.SetTaskTypeArchived(@default.Id, true));
    }

    [Fact]
    public void SetTaskTypeArchived_Should_ToggleBothWays()
    {
        var board = Board.Create("Flow", "FLW");
        var epic = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Epic);

        board.SetTaskTypeArchived(epic.Id, true);
        Assert.True(epic.IsArchived);

        board.SetTaskTypeArchived(epic.Id, false);
        Assert.False(epic.IsArchived);
    }

    [Fact]
    public void CreateTask_Should_UseDefaultType_When_TypeNotGiven()
    {
        var board = Board.Create("Flow", "FLW");

        var task = board.CreateTask("Задача");

        Assert.Equal(board.TaskTypes.Single(t => t.IsDefault).Id, task.TypeId);
    }

    [Fact]
    public void CreateTask_Should_UseGivenType()
    {
        var board = Board.Create("Flow", "FLW");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);

        var task = board.CreateTask("Падает вход", typeId: bug.Id);

        Assert.Equal(bug.Id, task.TypeId);
    }

    [Fact]
    public void CreateTask_Should_Throw_And_KeepCounter_When_TypeArchivedOrForeign()
    {
        var board = Board.Create("Flow", "FLW");
        var epic = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Epic);
        board.SetTaskTypeArchived(epic.Id, true);
        var foreign = Board.Create("Other", "OTH").TaskTypes.First();

        Assert.Throws<InvalidOperationException>(() => board.CreateTask("A", typeId: epic.Id));
        Assert.Throws<InvalidOperationException>(() => board.CreateTask("B", typeId: foreign.Id));
        Assert.Equal(0, board.NextTaskNumber);
    }
}
