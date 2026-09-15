using Flow.Application.Abstractions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Tests.Fakes;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Точки постановки в очередь индексации: что попадает в индекс и, не менее важно, что в него не попадает.
/// Транзакционность самой записи проверяют интеграционные тесты — здесь только поведение хендлеров.
/// </summary>
public class SearchIndexQueueFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator, FakeBoardRepository boards, FakeTaskItemRepository tasks)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!;
        tasks.RegisterBoardStatuses((await boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return board;
    }

    private static async Task<TaskResponse> CreateTaskAsync(IMediator mediator, Guid boardId) =>
        (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Экспорт падает", "Описание", null), CancellationToken.None))!;

    [Fact]
    public async Task CreateBoard_Should_Enqueue_Board()
    {
        var (mediator, _, _, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();

        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!;

        var request = Assert.Single(searchIndex.For(SearchSourceType.Board, board.Id));
        Assert.Equal(SearchIndexOperation.Upsert, request.Operation);
        Assert.Equal(board.Id, request.BoardId);
        Assert.Equal(0, request.Priority);
    }

    [Fact]
    public async Task RenameBoard_Should_Enqueue_Board()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        searchIndex.Clear();

        await mediator.Send(new BoardRenameCommand(Owner, board.Id, "Flow 2"), CancellationToken.None);

        var request = Assert.Single(searchIndex.For(SearchSourceType.Board, board.Id));
        Assert.Equal(SearchIndexOperation.Upsert, request.Operation);
    }

    [Fact]
    public async Task DeleteBoard_Should_Enqueue_Single_Delete()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        await CreateTaskAsync(mediator, board.Id);
        searchIndex.Clear();

        await mediator.Send(new BoardDeleteCommand(Owner, board.Id), CancellationToken.None);

        // Одной записи хватает: чанки задач и комментариев воркер уберёт по BoardId.
        var request = Assert.Single(searchIndex.All);
        Assert.Equal(SearchSourceType.Board, request.SourceType);
        Assert.Equal(SearchIndexOperation.Delete, request.Operation);
    }

    [Fact]
    public async Task CreateTask_Should_Enqueue_Task()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        searchIndex.Clear();

        var task = await CreateTaskAsync(mediator, board.Id);

        var request = Assert.Single(searchIndex.For(SearchSourceType.Task, task.Id));
        Assert.Equal(SearchIndexOperation.Upsert, request.Operation);
        Assert.Equal(board.Id, request.BoardId);
    }

    [Fact]
    public async Task UpdateTask_Should_Enqueue_Task_When_Title_Changed()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        searchIndex.Clear();

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Другое название", null, null), CancellationToken.None);

        Assert.Single(searchIndex.For(SearchSourceType.Task, task.Id));
    }

    [Fact]
    public async Task UpdateTask_Should_Not_Enqueue_When_Nothing_Changed()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        searchIndex.Clear();

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, task.Title, task.Description, null), CancellationToken.None);

        Assert.Empty(searchIndex.All);
    }

    [Fact]
    public async Task UpdateTask_Should_Enqueue_When_Status_Changed()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var done = board.Statuses.First(s => s.IsFinal).Id;
        searchIndex.Clear();

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, null, null, done), CancellationToken.None);

        // Текст не менялся: воркер увидит тот же ContentHash и обновит только IsClosed, без реэмбеддинга.
        var request = Assert.Single(searchIndex.For(SearchSourceType.Task, task.Id));
        Assert.Equal(SearchIndexOperation.Upsert, request.Operation);
    }

    [Fact]
    public async Task Assign_And_DueDate_Should_Not_Touch_Index()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        searchIndex.Clear();

        await mediator.Send(new TaskAssignCommand(Owner, task.Id, Owner), CancellationToken.None);
        await mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, new DateOnly(2026, 1, 1)), CancellationToken.None);

        // Ни исполнитель, ни срок текста не меняют — в индексе им делать нечего.
        Assert.Empty(searchIndex.All);
    }

    [Fact]
    public async Task DeleteTask_Should_Enqueue_Task_And_Its_Comments()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var comment = (await mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "Первый"), CancellationToken.None)).Response!;
        searchIndex.Clear();

        await mediator.Send(new TaskDeleteCommand(Owner, task.Id), CancellationToken.None);

        Assert.Equal(SearchIndexOperation.Delete, Assert.Single(searchIndex.For(SearchSourceType.Task, task.Id)).Operation);
        Assert.Equal(SearchIndexOperation.Delete, Assert.Single(searchIndex.For(SearchSourceType.Comment, comment.Id)).Operation);
    }

    [Fact]
    public async Task Comment_Should_Be_Enqueued_On_Add_Edit_And_Delete()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        var comment = (await mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "Первый"), CancellationToken.None)).Response!;
        Assert.Equal(SearchIndexOperation.Upsert, Assert.Single(searchIndex.For(SearchSourceType.Comment, comment.Id)).Operation);

        searchIndex.Clear();
        await mediator.Send(new TaskCommentEditCommand(Owner, comment.Id, "Второй"), CancellationToken.None);
        Assert.Single(searchIndex.For(SearchSourceType.Comment, comment.Id));

        searchIndex.Clear();
        await mediator.Send(new TaskCommentDeleteCommand(Owner, comment.Id), CancellationToken.None);
        Assert.Equal(SearchIndexOperation.Delete, Assert.Single(searchIndex.For(SearchSourceType.Comment, comment.Id)).Operation);
    }

    [Fact]
    public async Task EditComment_Should_Not_Enqueue_When_Body_Is_The_Same()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var comment = (await mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "Первый"), CancellationToken.None)).Response!;
        searchIndex.Clear();

        await mediator.Send(new TaskCommentEditCommand(Owner, comment.Id, "Первый"), CancellationToken.None);

        Assert.Empty(searchIndex.All);
    }

    [Fact]
    public async Task UpdateProfile_Should_Enqueue_User()
    {
        var (mediator, _, _, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();

        await mediator.Send(new UserUpdateProfileCommand(Owner, Owner, "Илья", null, null, null, null, null), CancellationToken.None);

        var request = Assert.Single(searchIndex.For(SearchSourceType.User, Owner));
        Assert.Equal(SearchIndexOperation.Upsert, request.Operation);
        Assert.Null(request.BoardId);
    }
}
