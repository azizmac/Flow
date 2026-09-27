using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Queries.TaskBoardQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;
using Xunit;
using ProjectRole = Flow.Domain.Entities.ProjectRole;
using SharedMode = Flow.Shared.Contracts.Boards.WorkflowMode;
using StatusType = Flow.Shared.Contracts.Boards.StatusType;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Канбан в Application (docs/TZ_task_views.md §1, этап 2B): колонки и счётчики, догрузка колонки, перенос карточки
/// со сменой статуса через workflow, журнал и очередь поиска, настройки колонок. Окно финальной колонки и порядок
/// по рангу зависят от SQL — они в Flow.Infrastructure.Tests.
/// </summary>
public class KanbanFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<(BoardResponse Board, Guid Todo, Guid Doing, Guid Done)> ArrangeAsync(IMediator mediator)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;
        Guid Id(string name) => board.Statuses.Single(s => s.Name == name).Id;
        return (board, Id("Не начата"), Id("В работе"), Id("Сделана"));
    }

    private static async Task<Guid> CreateAsync(IMediator mediator, Guid boardId, string title, Guid? statusId = null) =>
        (await mediator.Send(new TaskCreateCommand(Owner, boardId, title, null, statusId), CancellationToken.None))!.Id;

    [Fact]
    public async Task Board_Has_Column_Per_Status_With_Counts_And_Pages()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var (board, todo, doing, _) = await ArrangeAsync(mediator);
        for (var i = 0; i < 3; i++)
            await CreateAsync(mediator, board.Id, $"Задача {i}");
        await CreateAsync(mediator, board.Id, "В работе", doing);

        var kanban = (await mediator.Send(new TaskBoardQuery(Owner, board.Id, Limit: 2), CancellationToken.None))!;

        Assert.Equal(board.Statuses.Select(s => s.Id), kanban.Columns.Select(c => c.StatusId!.Value));
        var todoColumn = kanban.Columns.Single(c => c.StatusId == todo);
        Assert.Equal(3, todoColumn.Count);
        Assert.Equal(2, todoColumn.Tasks.Count);
        Assert.Equal(1, kanban.Columns.Single(c => c.StatusId == doing).Count);
        Assert.Equal(14, kanban.DoneColumnDays);

        // Догрузка одной колонки — только она, со следующей страницы.
        var more = (await mediator.Send(new TaskBoardQuery(Owner, board.Id, StatusId: todo, Offset: 2, Limit: 2), CancellationToken.None))!;
        var column = Assert.Single(more.Columns);
        Assert.Single(column.Tasks);
        Assert.DoesNotContain(column.Tasks[0].Id, todoColumn.Tasks.Select(t => t.Id));
    }

    [Fact]
    public async Task All_Projects_Board_Groups_By_Status_Type_With_Other_Column()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await ArrangeAsync(mediator);

        var kanban = (await mediator.Send(new TaskBoardQuery(Owner), CancellationToken.None))!;

        Assert.Null(kanban.BoardId);
        Assert.Null(kanban.DoneColumnDays);
        Assert.Equal([StatusType.NotStarted, StatusType.InProgress, StatusType.InReview, StatusType.Done, null], kanban.Columns.Select(c => c.StatusType));
        Assert.True(kanban.Columns[^1].Other);
    }

    [Fact]
    public async Task Hidden_Or_Missing_Project_Board_Is_Null()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        Assert.Null(await mediator.Send(new TaskBoardQuery(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Moving_To_Another_Column_Changes_Status_Journal_And_Index()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var (board, todo, doing, _) = await ArrangeAsync(mediator);
        var task = await CreateAsync(mediator, board.Id, "Задача");
        var neighbour = await CreateAsync(mediator, board.Id, "Сосед", doing);

        var moved = await mediator.Send(new TaskRankCommand(Owner, task, neighbour, null, doing), CancellationToken.None);

        Assert.Equal(doing, moved.Response!.StatusId);
        var entry = Assert.Single(activities.ForTask(task), a => a.Type == TaskActivityType.StatusChanged);
        Assert.Equal(todo.ToString(), entry.OldValue);
    }

    [Fact]
    public async Task Moving_Into_Empty_Column_Needs_No_Neighbours_And_Queues_Index()
    {
        var (mediator, _, _, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var (board, _, doing, _) = await ArrangeAsync(mediator);
        var task = await CreateAsync(mediator, board.Id, "Задача");
        searchIndex.Clear();

        var moved = await mediator.Send(new TaskRankCommand(Owner, task, null, null, doing), CancellationToken.None);

        Assert.Equal(doing, moved.Response!.StatusId);
        Assert.Contains(searchIndex.For(SearchSourceType.Task, task), r => r.Operation == SearchIndexOperation.Upsert);
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskRankCommand(Owner, task, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Moving_Against_Workflow_Is_Refused_Without_Changes()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var (board, todo, doing, done) = await ArrangeAsync(mediator);
        await mediator.Send(new WorkflowSetCommand(Owner, board.Id, SharedMode.Restricted,
            [new TransitionRequest(todo, doing), new TransitionRequest(doing, done), new TransitionRequest(null, todo)]), CancellationToken.None);
        var task = await CreateAsync(mediator, board.Id, "Задача");

        var refused = await mediator.Send(new TaskRankCommand(Owner, task, null, null, done), CancellationToken.None);

        Assert.Null(refused.Response);
        Assert.Contains("«Не начата» → «Сделана»", refused.Reasons!.Single());
        Assert.DoesNotContain(activities.ForTask(task), a => a.Type == TaskActivityType.StatusChanged);
    }

    [Fact]
    public async Task Viewer_Cannot_Move_Cards()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var viewer = User.Create("viewer", "viewer@example.com", "Имя", "Фамилия");
        viewer.ChangeRole(UserRole.Reader);
        viewer.MarkActive();
        users.Add(viewer);
        var (board, _, doing, _) = await ArrangeAsync(mediator);
        var task = await CreateAsync(mediator, board.Id, "Задача");

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new TaskRankCommand(viewer.Id, task, null, null, doing), CancellationToken.None));
    }

    [Fact]
    public async Task Wip_Limit_And_Done_Window_Are_Project_Config()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        users.Add(member);
        var (board, _, doing, _) = await ArrangeAsync(mediator);

        var limited = (await mediator.Send(new StatusUpdateCommand(Owner, board.Id, doing, WipLimit: 3), CancellationToken.None))!;
        Assert.Equal(3, limited.Statuses.Single(s => s.Id == doing).WipLimit);
        var cleared = (await mediator.Send(new StatusUpdateCommand(Owner, board.Id, doing, ClearWipLimit: true), CancellationToken.None))!;
        Assert.Null(cleared.Statuses.Single(s => s.Id == doing).WipLimit);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new StatusUpdateCommand(Owner, board.Id, doing, WipLimit: 0), CancellationToken.None));

        Assert.Equal(30, (await mediator.Send(new BoardDoneColumnDaysSetCommand(Owner, board.Id, 30), CancellationToken.None))!.DoneColumnDays);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardDoneColumnDaysSetCommand(Owner, board.Id, 0), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new BoardDoneColumnDaysSetCommand(member.Id, board.Id, 7), CancellationToken.None));
    }
}
