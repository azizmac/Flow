using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Xunit;

namespace Flow.Application.Tests.Features;

public class BoardFeatureTests
{
    [Fact]
    public async Task CreateBoard_Should_ReturnBoardWithDefaultStatuses()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var response = await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None);

        Assert.Equal("FLW", response.Key);
        Assert.Equal("Flow Project", response.Name);
        Assert.Equal(4, response.Statuses.Count);
    }

    [Fact]
    public async Task CreateBoard_Should_Throw_When_KeyIsInvalid()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardCreateCommand("Bad board", "bad-key!"), CancellationToken.None));
    }

    [Fact]
    public async Task GetBoards_Should_ReturnAllCreatedBoards()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();
        await mediator.Send(new BoardCreateCommand("Board One", "ONE"), CancellationToken.None);
        await mediator.Send(new BoardCreateCommand("Board Two", "TWO"), CancellationToken.None);

        var boards = await mediator.Send(new BoardListQuery(), CancellationToken.None);

        Assert.Equal(2, boards.Count);
    }

    [Fact]
    public async Task RenameBoard_Should_UpdateName_When_BoardExists()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();
        var created = await mediator.Send(new BoardCreateCommand("Old name", "FLW"), CancellationToken.None);

        var response = await mediator.Send(new BoardRenameCommand(created.Id, "New name"), CancellationToken.None);

        Assert.NotNull(response);
        Assert.Equal("New name", response!.Name);

        var refetched = await mediator.Send(new BoardGetQuery(created.Id), CancellationToken.None);
        Assert.Equal("New name", refetched!.Name);
    }

    [Fact]
    public async Task RenameBoard_Should_ReturnNull_When_BoardNotFound()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var response = await mediator.Send(new BoardRenameCommand(Guid.NewGuid(), "New name"), CancellationToken.None);

        Assert.Null(response);
    }

    [Fact]
    public async Task RenameBoard_Should_Throw_When_NameIsEmpty()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();
        var created = await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardRenameCommand(created.Id, ""), CancellationToken.None));
    }

    [Fact]
    public async Task DeleteBoard_Should_RemoveBoard_When_Exists()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();
        var created = await mediator.Send(new BoardCreateCommand("Flow Project", "FLW"), CancellationToken.None);

        var deleted = await mediator.Send(new BoardDeleteCommand(created.Id), CancellationToken.None);

        Assert.True(deleted);
        var afterDelete = await mediator.Send(new BoardGetQuery(created.Id), CancellationToken.None);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task DeleteBoard_Should_ReturnFalse_When_NotFound()
    {
        var (mediator, _, _) = TestMediatorFactory.Create();

        var deleted = await mediator.Send(new BoardDeleteCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(deleted);
    }
}
