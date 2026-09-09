using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class BoardTests
{
    [Fact]
    public void Create_Should_SeedFourDefaultStatuses()
    {
        var board = Board.Create("Flow Project", "FLW");

        Assert.Equal(4, board.Statuses.Count);
        Assert.Contains(board.Statuses, s => s is { Name: "Не начата", IsInitial: true, IsFinal: false, Type: StatusType.NotStarted });
        Assert.Contains(board.Statuses, s => s is { Name: "В работе", IsInitial: false, IsFinal: false, Type: StatusType.InProgress });
        Assert.Contains(board.Statuses, s => s is { Name: "На проверке", IsInitial: false, IsFinal: false, Type: StatusType.InReview });
        Assert.Contains(board.Statuses, s => s is { Name: "Сделана", IsInitial: false, IsFinal: true, Type: StatusType.Done });
    }

    [Fact]
    public void AddStatus_Should_LeaveTypeNull_ForCustomStatus()
    {
        var board = Board.Create("Flow Project", "FLW");

        var custom = board.AddStatus("Заблокирована");

        Assert.Null(custom.Type);
    }

    [Fact]
    public void Create_Should_NormalizeKeyToUpperCase()
    {
        var board = Board.Create("Flow Project", "flw");

        Assert.Equal("FLW", board.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("f")]
    [InlineData("FL-W")]
    [InlineData("FLOWWWWWWWW")]
    [InlineData("1FLW")]
    public void Create_Should_Throw_When_KeyIsInvalid(string invalidKey)
    {
        Assert.Throws<ArgumentException>(() => Board.Create("Flow Project", invalidKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Should_Throw_When_NameIsEmpty(string invalidName)
    {
        Assert.Throws<ArgumentException>(() => Board.Create(invalidName, "FLW"));
    }

    [Fact]
    public void Rename_Should_UpdateName()
    {
        var board = Board.Create("Old name", "FLW");

        board.Rename("New name");

        Assert.Equal("New name", board.Name);
    }

    [Fact]
    public void Rename_Should_Throw_When_NameIsEmpty()
    {
        var board = Board.Create("Flow Project", "FLW");

        Assert.Throws<ArgumentException>(() => board.Rename(""));
    }

    [Fact]
    public void CreateTask_Should_UseInitialStatus_When_StatusIdNotProvided()
    {
        var board = Board.Create("Flow Project", "FLW");
        var initialStatus = board.Statuses.Single(s => s.IsInitial);

        var task = board.CreateTask("Test task");

        Assert.Equal(initialStatus.Id, task.StatusId);
    }

    [Fact]
    public void CreateTask_Should_GenerateSequentialCodes()
    {
        var board = Board.Create("Flow Project", "FLW");

        var first = board.CreateTask("First");
        var second = board.CreateTask("Second");

        Assert.Equal("FLW-1", first.Code.Value);
        Assert.Equal("FLW-2", second.Code.Value);
    }

    [Fact]
    public void CreateTask_Should_Throw_When_StatusDoesNotBelongToBoard()
    {
        var board = Board.Create("Flow Project", "FLW");
        var foreignStatusId = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => board.CreateTask("Test task", statusId: foreignStatusId));
    }

    [Fact]
    public void AddStatus_Should_Throw_When_SecondInitialStatusAdded()
    {
        var board = Board.Create("Flow Project", "FLW");

        Assert.Throws<InvalidOperationException>(() => board.AddStatus("Another initial", isInitial: true));
    }

    [Fact]
    public void AddStatus_Should_Throw_When_SecondFinalStatusAdded()
    {
        var board = Board.Create("Flow Project", "FLW");

        Assert.Throws<InvalidOperationException>(() => board.AddStatus("Another final", isFinal: true));
    }

    [Fact]
    public void SetInitialStatus_Should_MoveInitialFlagToAnotherStatus()
    {
        var board = Board.Create("Flow Project", "FLW");
        var oldInitial = board.Statuses.Single(s => s.IsInitial);
        var newInitial = board.Statuses.Single(s => s.Name == "В работе");

        board.SetInitialStatus(newInitial.Id);

        Assert.False(oldInitial.IsInitial);
        Assert.True(newInitial.IsInitial);

        var task = board.CreateTask("Test task");
        Assert.Equal(newInitial.Id, task.StatusId);
    }

    [Fact]
    public void CreateTask_Should_Store_CreatedById()
    {
        var board = Board.Create("Flow", "FLW");
        var actor = Guid.NewGuid();

        var task = board.CreateTask("Test task", createdById: actor);

        Assert.Equal(actor, task.CreatedById);
    }
}
