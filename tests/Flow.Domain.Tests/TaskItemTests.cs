using Flow.Domain.Entities;
using Xunit;

namespace Flow.Domain.Tests;

public class TaskItemTests
{
    private static (Board Board, TaskItem Task) CreateBoardWithTask()
    {
        var board = Board.Create("Flow Project", "FLW");
        var task = board.CreateTask("Test task", "Initial description");
        Assert.Null(task.CreatedById);
        return (board, task);
    }

    [Fact]
    public void Rename_Should_UpdateTitle()
    {
        var (_, task) = CreateBoardWithTask();

        task.Rename("Renamed task");

        Assert.Equal("Renamed task", task.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rename_Should_Throw_When_TitleIsEmpty(string invalidTitle)
    {
        var (_, task) = CreateBoardWithTask();

        Assert.Throws<ArgumentException>(() => task.Rename(invalidTitle));
    }

    [Fact]
    public void UpdateDescription_Should_AllowSettingNull()
    {
        var (_, task) = CreateBoardWithTask();

        task.UpdateDescription(null);

        Assert.Null(task.Description);
    }

    [Fact]
    public void ChangeStatus_Should_UpdateStatusId()
    {
        var (board, task) = CreateBoardWithTask();
        var doneStatus = board.Statuses.Single(s => s.IsFinal);

        task.ChangeStatus(doneStatus.Id);

        Assert.Equal(doneStatus.Id, task.StatusId);
    }

    [Fact]
    public void Assign_Then_Unassign_Should_ToggleAssigneeId()
    {
        var (_, task) = CreateBoardWithTask();
        var userId = Guid.NewGuid();

        Assert.Null(task.AssigneeId);

        task.Assign(userId);
        Assert.Equal(userId, task.AssigneeId);

        task.Unassign();
        Assert.Null(task.AssigneeId);
    }

    [Fact]
    public void Assign_Should_Throw_When_UserIdIsEmpty()
    {
        var (_, task) = CreateBoardWithTask();

        Assert.Throws<ArgumentException>(() => task.Assign(Guid.Empty));
    }
}
