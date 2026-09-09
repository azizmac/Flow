using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

public class TaskAssignFeatureTests
{
    private static async Task<(Guid BoardId, TaskResponse Task, UserResponse User)> SetupAsync(IMediator mediator)
    {
        var board = (await mediator.Send(new BoardCreateCommand(TestMediatorFactory.OwnerId, "Flow Project", "FLW"), CancellationToken.None)).Response!;
        var task = (await mediator.Send(new TaskCreateCommand(TestMediatorFactory.OwnerId, board.Id, "Task", null, null), CancellationToken.None))!;
        var user = (await mediator.Send(new UserCreateCommand(TestMediatorFactory.OwnerId, "ilya", "ilya@example.com", "Илья", "Моторин", "correct horse battery"), CancellationToken.None)).Response!;
        return (board.Id, task, user);
    }

    [Fact]
    public async Task Assign_Should_SetAssigneeId()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, task, user) = await SetupAsync(mediator);

        var result = await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, user.Id), CancellationToken.None);

        Assert.False(result.IsNotFound);
        Assert.Null(result.ValidationError);
        Assert.Equal(user.Id, result.Response!.AssigneeId);
    }

    [Fact]
    public async Task Assign_Should_ReturnValidationError_When_UserInactive()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, task, user) = await SetupAsync(mediator);
        await mediator.Send(new UserDeactivateCommand(TestMediatorFactory.OwnerId, user.Id), CancellationToken.None);

        var result = await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, user.Id), CancellationToken.None);

        Assert.NotNull(result.ValidationError);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Assign_Should_ReturnValidationError_When_UserMissing()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, task, _) = await SetupAsync(mediator);

        var result = await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, Guid.NewGuid()), CancellationToken.None);

        Assert.NotNull(result.ValidationError);
    }

    [Fact]
    public async Task Assign_Should_ReturnNotFound_When_TaskMissing()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, _, user) = await SetupAsync(mediator);

        var result = await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, Guid.NewGuid(), user.Id), CancellationToken.None);

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task Assign_WithNull_Should_Unassign()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (_, task, user) = await SetupAsync(mediator);
        await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, user.Id), CancellationToken.None);

        var result = await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, null), CancellationToken.None);

        Assert.Null(result.Response!.AssigneeId);
    }

    [Fact]
    public async Task TaskList_Should_FilterByAssignee()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (boardId, task, user) = await SetupAsync(mediator);
        await mediator.Send(new TaskCreateCommand(TestMediatorFactory.OwnerId, boardId, "Unassigned", null, null), CancellationToken.None);
        await mediator.Send(new TaskAssignCommand(TestMediatorFactory.OwnerId, task.Id, user.Id), CancellationToken.None);

        var all = await mediator.Send(new TaskListQuery(boardId), CancellationToken.None);
        var mine = await mediator.Send(new TaskListQuery(boardId, user.Id), CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Equal(task.Id, Assert.Single(mine).Id);
    }
}
