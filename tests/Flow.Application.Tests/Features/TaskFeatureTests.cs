using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Tests.Fakes;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Ids;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

public class TaskFeatureTests
{
    private static async Task<BoardResponse> CreateBoardAsync(
        IMediator mediator, FakeBoardRepository boards, FakeTaskItemRepository tasks)
    {
        var board = (await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None)).Response!;

        // FakeTaskItemRepository.StatusBelongsToBoardAsync нужно явно "заселить" статусами доски,
        // т.к. в отличие от реального EF Core у фейка нет общей таблицы Statuses.
        var domainBoard = await boards.GetByIdAsync(board.Id, CancellationToken.None);
        tasks.RegisterBoardStatuses(domainBoard!);

        return board;
    }

    [Fact]
    public async Task CreateTask_Should_ReturnTaskWithGeneratedCode_And_DefaultStatus()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var initialStatusId = board.Statuses.Single(s => s.IsInitial).Id;

        var response = await mediator.Send(
            new TaskCreateCommand(board.Id, "Test task", "desc", null),
            CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("FLW-1", response!.Code);
        Assert.Equal("Test task", response.Title);
        Assert.Equal("desc", response.Description);
        Assert.Equal(initialStatusId, response.StatusId);
    }

    [Fact]
    public async Task CreateTask_Should_ReturnNull_When_BoardNotFound()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var response = await mediator.Send(
            new TaskCreateCommand(BoardId.New(), "Test task", null, null),
            CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task CreateTask_Should_GenerateSequentialCodes()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);

        var first = await mediator.Send(new TaskCreateCommand(board.Id, "First", null, null), CancellationToken.None);
        var second = await mediator.Send(new TaskCreateCommand(board.Id, "Second", null, null), CancellationToken.None);

        Assert.Equal("FLW-1", first!.Code);
        Assert.Equal("FLW-2", second!.Code);
    }

    [Fact]
    public async Task GetBoardTasks_Should_ReturnOnlyTasksOfThatBoard()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var boardA = await CreateBoardAsync(mediator, boards, tasks);
        var boardB = (await mediator.Send(new BoardCreateCommand("Other board", "OTH"), CancellationToken.None)).Response!;
        await mediator.Send(new TaskCreateCommand(boardA.Id, "Task A", null, null), CancellationToken.None);
        await mediator.Send(new TaskCreateCommand(boardB.Id, "Task B", null, null), CancellationToken.None);

        var boardATasks = await mediator.Send(new TaskListQuery(boardA.Id), CancellationToken.None);

        Assert.Single(boardATasks);
        Assert.Equal("Task A", boardATasks[0].Title);
    }

    [Fact]
    public async Task UpdateTask_Should_RenameTitle()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var created = await mediator.Send(new TaskCreateCommand(board.Id, "Old title", null, null), CancellationToken.None);

        var result = await mediator.Send(
            new TaskUpdateCommand(created!.Id, "New title", null, null),
            CancellationToken.None);

        Assert.False(result.IsNotFound);
        Assert.Null(result.ValidationError);
        Assert.Equal("New title", result.Response!.Title);

        var refetched = await mediator.Send(new TaskGetQuery(created.Id), CancellationToken.None);
        Assert.Equal("New title", refetched!.Title);
    }

    [Fact]
    public async Task UpdateTask_Should_ReturnNotFound_When_TaskMissing()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var result = await mediator.Send(
            new TaskUpdateCommand(TaskId.New(), "New title", null, null),
            CancellationToken.None);

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task UpdateTask_Should_ReturnValidationError_When_StatusBelongsToAnotherBoard()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var boardA = await CreateBoardAsync(mediator, boards, tasks);
        var boardB = (await mediator.Send(new BoardCreateCommand("Other board", "OTH"), CancellationToken.None)).Response!;
        var domainBoardB = await boards.GetByIdAsync(boardB.Id, CancellationToken.None);
        tasks.RegisterBoardStatuses(domainBoardB!);

        var created = await mediator.Send(new TaskCreateCommand(boardA.Id, "Task A", null, null), CancellationToken.None);
        var foreignStatusId = boardB.Statuses[0].Id;

        var result = await mediator.Send(
            new TaskUpdateCommand(created!.Id, null, null, foreignStatusId),
            CancellationToken.None);

        Assert.False(result.IsNotFound);
        Assert.NotNull(result.ValidationError);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task UpdateTask_Should_ChangeStatus_When_StatusBelongsToSameBoard()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var created = await mediator.Send(new TaskCreateCommand(board.Id, "Task", null, null), CancellationToken.None);
        var doneStatusId = board.Statuses.Single(s => s.IsFinal).Id;

        var result = await mediator.Send(
            new TaskUpdateCommand(created!.Id, null, null, doneStatusId),
            CancellationToken.None);

        Assert.Equal(doneStatusId, result.Response!.StatusId);
    }

    [Fact]
    public async Task DeleteTask_Should_RemoveTask_When_Exists()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var created = await mediator.Send(new TaskCreateCommand(board.Id, "Task", null, null), CancellationToken.None);

        var deleted = await mediator.Send(new TaskDeleteCommand(created!.Id), CancellationToken.None);

        Assert.True(deleted);
        var afterDelete = await mediator.Send(new TaskGetQuery(created.Id), CancellationToken.None);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task DeleteTask_Should_ReturnFalse_When_NotFound()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var deleted = await mediator.Send(new TaskDeleteCommand(TaskId.New()), CancellationToken.None);

        Assert.False(deleted);
    }
}
