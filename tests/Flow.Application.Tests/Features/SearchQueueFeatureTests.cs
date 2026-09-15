using Flow.Application.Abstractions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Tests.Fakes;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Что хендлеры ставят в очередь переиндексации (ТЗ поиска, этап 3.1). Проверяется именно «что и когда
/// ставится»: тексты, чанки и векторы — дело Flow.Infrastructure.
/// </summary>
public class SearchQueueFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<Guid> CreateBoardAsync(IMediator mediator, FakeTaskItemRepository tasks, FakeBoardRepository boards)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Флоу", "FLW"), CancellationToken.None)).Response!;
        tasks.RegisterBoardStatuses((await boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return board.Id;
    }

    [Fact]
    public async Task CreateTask_Should_Enqueue_Upsert()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearch();
        var boardId = await CreateBoardAsync(mediator, tasks, boards);

        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Экспорт PDF", null, null), CancellationToken.None))!;

        Assert.True(searchIndex.Contains(SearchSourceType.Task, task.Id, SearchIndexOperation.Upsert));
    }

    [Fact]
    public async Task UpdateTask_Should_Enqueue_Only_When_Text_Or_Status_Changed()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearch();
        var boardId = await CreateBoardAsync(mediator, tasks, boards);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Экспорт PDF", null, null), CancellationToken.None))!;
        searchIndex.Clear();

        // То же название — правки нет, значит и переиндексировать нечего.
        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Экспорт PDF", null, null), CancellationToken.None);
        Assert.Empty(searchIndex.Requests);

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Экспорт в PDF", null, null), CancellationToken.None);
        Assert.True(searchIndex.Contains(SearchSourceType.Task, task.Id, SearchIndexOperation.Upsert));
    }

    [Fact]
    public async Task AssignAndDueDate_Should_Not_Touch_Index()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearch();
        var boardId = await CreateBoardAsync(mediator, tasks, boards);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Экспорт PDF", null, null), CancellationToken.None))!;
        searchIndex.Clear();

        await mediator.Send(new TaskAssignCommand(Owner, task.Id, Owner), CancellationToken.None);
        await mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, new DateOnly(2026, 12, 31)), CancellationToken.None);

        // Ни исполнитель, ни срок не меняют текст — в индексе им делать нечего.
        Assert.Empty(searchIndex.Requests);
    }

    [Fact]
    public async Task DeleteTask_Should_Enqueue_Delete_For_Task_And_Its_Comments()
    {
        var (mediator, boards, tasks, _, comments, searchIndex) = TestMediatorFactory.CreateWithSearch();
        var boardId = await CreateBoardAsync(mediator, tasks, boards);
        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Экспорт PDF", null, null), CancellationToken.None))!;
        var comment = (await mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "не воспроизводится"), CancellationToken.None)).Response!;
        searchIndex.Clear();

        await mediator.Send(new TaskDeleteCommand(Owner, task.Id), CancellationToken.None);

        Assert.True(searchIndex.Contains(SearchSourceType.Task, task.Id, SearchIndexOperation.Delete));
        // Комментарии уйдут каскадом БД, но их чанки надо снести отдельно — id после удаления не узнать.
        Assert.True(searchIndex.Contains(SearchSourceType.Comment, comment.Id, SearchIndexOperation.Delete));
        Assert.NotEmpty(comments.All);
    }

    [Fact]
    public async Task Board_Commands_Should_Enqueue_Board()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearch();
        var boardId = await CreateBoardAsync(mediator, tasks, boards);

        Assert.True(searchIndex.Contains(SearchSourceType.Board, boardId, SearchIndexOperation.Upsert));
        searchIndex.Clear();

        await mediator.Send(new BoardRenameCommand(Owner, boardId, "Флоу 2"), CancellationToken.None);
        Assert.True(searchIndex.Contains(SearchSourceType.Board, boardId, SearchIndexOperation.Upsert));
        searchIndex.Clear();

        await mediator.Send(new BoardDeleteCommand(Owner, boardId), CancellationToken.None);
        Assert.True(searchIndex.Contains(SearchSourceType.Board, boardId, SearchIndexOperation.Delete));
    }
}
