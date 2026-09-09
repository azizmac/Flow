using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Xunit;

namespace Flow.Application.Tests.Features;

public class BoardFeatureTests
{
    [Fact]
    public async Task CreateBoard_Should_ReturnBoardWithDefaultStatuses()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var result = await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None);

        Assert.False(result.IsKeyTaken);
        var response = result.Response!;
        Assert.Equal("FLW", response.Key);
        Assert.Equal("Flow Project", response.Name);
        Assert.Equal(4, response.Statuses.Count);
    }

    [Fact]
    public async Task CreateBoard_Should_Throw_When_KeyIsInvalid()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardCreateCommand("Bad board", "bad-key!"), CancellationToken.None));
    }

    [Fact]
    public async Task GetBoards_Should_ReturnAllCreatedBoards()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand("Board One", "ONE"), CancellationToken.None);
        await mediator.Send(new BoardCreateCommand("Board Two", "TWO"), CancellationToken.None);

        var boards = await mediator.Send(new BoardListQuery(), CancellationToken.None);

        Assert.Equal(2, boards.Count);
    }

    [Fact]
    public async Task RenameBoard_Should_UpdateName_When_BoardExists()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = (await mediator.Send(new BoardCreateCommand("Old name", "FLW"), CancellationToken.None)).Response!;

        var response = await mediator.Send(new BoardRenameCommand(created.Id, "New name"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("New name", response!.Name);

        var refetched = await mediator.Send(new BoardGetQuery(created.Id), CancellationToken.None);
        Assert.Equal("New name", refetched!.Name);
    }

    [Fact]
    public async Task RenameBoard_Should_ReturnNull_When_BoardNotFound()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var response = await mediator.Send(new BoardRenameCommand(Guid.NewGuid(), "New name"), CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task RenameBoard_Should_Throw_When_NameIsEmpty()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = (await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None)).Response!;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardRenameCommand(created.Id, ""), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteBoard_Should_RemoveBoard_When_Exists()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = (await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None)).Response!;

        var deleted = await mediator.Send(new BoardDeleteCommand(created.Id), CancellationToken.None);

        Assert.True(deleted);
        var afterDelete = await mediator.Send(new BoardGetQuery(created.Id), CancellationToken.None);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task DeleteBoard_Should_ReturnFalse_When_NotFound()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var deleted = await mediator.Send(new BoardDeleteCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(deleted);
    }

    [Fact]
    public async Task CreateBoard_Should_ReturnKeyTaken_When_KeyAlreadyExists()
    {
        var (mediator, boards, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand("First", "FLW"), CancellationToken.None);

        var result = await mediator.Send(new BoardCreateCommand("Second", "FLW"), CancellationToken.None);

        Assert.True(result.IsKeyTaken);
        Assert.Null(result.Response);
        Assert.Contains("FLW", result.ValidationError);
        Assert.Single(await boards.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CreateBoard_Should_ReturnKeyTaken_When_KeyDiffersOnlyByCase()
    {
        // Board.Create нормализует ключ в верхний регистр, поэтому "flw" и "FLW" — одна доска.
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand("First", "FLW"), CancellationToken.None);

        var result = await mediator.Send(new BoardCreateCommand("Second", " flw "), CancellationToken.None);

        Assert.True(result.IsKeyTaken);
    }

    [Fact]
    public async Task CreateBoard_Should_Succeed_When_KeysDiffer()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand("First", "ONE"), CancellationToken.None);

        var result = await mediator.Send(new BoardCreateCommand("Second", "TWO"), CancellationToken.None);

        Assert.False(result.IsKeyTaken);
        Assert.Equal("TWO", result.Response!.Key);
    }

    [Fact]
    public async Task DeleteBoard_Should_RemoveBoard_When_BoardHasTasks()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var created = (await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None)).Response!;
        var domainBoard = await boards.GetByIdAsync(created.Id, CancellationToken.None);
        tasks.RegisterBoardStatuses(domainBoard!);
        await mediator.Send(new TaskCreateCommand(created.Id, "Task 1", null, null), CancellationToken.None);
        await mediator.Send(new TaskCreateCommand(created.Id, "Task 2", null, null), CancellationToken.None);

        var deleted = await mediator.Send(new BoardDeleteCommand(created.Id), CancellationToken.None);

        Assert.True(deleted);
        Assert.Null(await mediator.Send(new BoardGetQuery(created.Id), CancellationToken.None));
    }

    [Fact]
    public async Task GetBoards_Should_ReturnTaskCountAndNextTaskNumber()
    {
        var (mediator, boards, tasks) = TestMediatorFactory.Create();
        var withTasks = (await mediator.Send(new BoardCreateCommand("With tasks", "WT"), CancellationToken.None)).Response!;
        var empty = (await mediator.Send(new BoardCreateCommand("Empty", "EMP"), CancellationToken.None)).Response!;
        tasks.RegisterBoardStatuses((await boards.GetByIdAsync(withTasks.Id, CancellationToken.None))!);
        await mediator.Send(new TaskCreateCommand(withTasks.Id, "Task 1", null, null), CancellationToken.None);
        await mediator.Send(new TaskCreateCommand(withTasks.Id, "Task 2", null, null), CancellationToken.None);

        var list = await mediator.Send(new BoardListQuery(), CancellationToken.None);
        var single = await mediator.Send(new BoardGetQuery(withTasks.Id), CancellationToken.None);

        Assert.Equal(2, list.Single(b => b.Id == withTasks.Id).TaskCount);
        Assert.Equal(3, list.Single(b => b.Id == withTasks.Id).NextTaskNumber);
        Assert.Equal(0, list.Single(b => b.Id == empty.Id).TaskCount);
        Assert.Equal(1, list.Single(b => b.Id == empty.Id).NextTaskNumber);
        Assert.Equal(2, single!.TaskCount);
        Assert.Equal(3, single.NextTaskNumber);
    }
}
