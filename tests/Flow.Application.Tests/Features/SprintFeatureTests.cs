using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintCompleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintDeleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintStartCommand;
using Flow.Application.Features.Sprints.Commands.SprintUpdateCommand;
using Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;
using Flow.Application.Features.Sprints.Queries.BacklogQuery;
using Flow.Application.Features.Sprints.Queries.SprintReportQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using SharedState = Flow.Shared.Contracts.Sprints.SprintState;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Спринты и бэклог в Application (docs/TZ_task_views.md §2, этап 2D): один активный, снимок при старте, перенос
/// незакрытых при завершении с журналом, удаление запланированного, поле спринта и перетаскивание, секции бэклога
/// с эпиками, отчёт. FQL `sprint` — биндер в FqlTests, SQL — в Flow.Infrastructure.Tests. Правила самого спринта — Flow.Domain.Tests, SQL и индекс — Flow.Infrastructure.Tests.
/// </summary>
public class SprintFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<BoardResponse> BoardAsync(IMediator mediator) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;

    private static async Task<Guid> TaskAsync(IMediator mediator, BoardResponse board, string title, SharedTypeKind kind = SharedTypeKind.Task, Guid? parent = null) =>
        (await mediator.Send(new TaskCreateCommand(Owner, board.Id, title, null, null, board.TaskTypes.First(t => t.Kind == kind).Id, ParentId: parent), CancellationToken.None))!.Id;

    private static async Task<Guid> SprintAsync(IMediator mediator, BoardResponse board, string? name = null) =>
        (await mediator.Send(new SprintCreateCommand(Owner, board.Id, name), CancellationToken.None))!.Id;

    private static Task<TaskUpdateResult> Plan(IMediator mediator, Guid task, Guid? sprint) =>
        mediator.Send(new TaskSetSprintCommand(Owner, task, sprint), CancellationToken.None);

    [Fact]
    public async Task Only_One_Active_Sprint_And_Start_Takes_Snapshot()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var first = await SprintAsync(mediator, board);
        var second = await SprintAsync(mediator, board);
        var task = await TaskAsync(mediator, board, "Задача");
        await mediator.Send(new TaskSetEstimateCommand(Owner, task, 3m, null), CancellationToken.None);
        await Plan(mediator, task, first);

        var started = (await mediator.Send(new SprintStartCommand(Owner, first, Today, Today.AddDays(14)), CancellationToken.None))!;
        Assert.Equal(SharedState.Active, started.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new SprintStartCommand(Owner, second, Today, Today.AddDays(14)), CancellationToken.None));

        var report = (await mediator.Send(new SprintReportQuery(Owner, first), CancellationToken.None))!;
        Assert.Equal((1, 3m), (report.Committed.Count, report.Committed.Points));
        Assert.Equal(0, report.Added.Count);
    }

    [Fact]
    public async Task Complete_Moves_Open_Tasks_With_Journal_And_Keeps_Done()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await BoardAsync(mediator);
        var sprint = await SprintAsync(mediator, board);
        var next = await SprintAsync(mediator, board);
        var open = await TaskAsync(mediator, board, "Открытая");
        var done = await TaskAsync(mediator, board, "Сделанная");
        await mediator.Send(new TaskSetEstimateCommand(Owner, open, 4m, null), CancellationToken.None);
        await Plan(mediator, open, sprint);
        await Plan(mediator, done, sprint);
        await mediator.Send(new SprintStartCommand(Owner, sprint, Today, Today.AddDays(7)), CancellationToken.None);
        await mediator.Send(new TaskUpdateCommand(Owner, done, null, null, board.Statuses.Single(s => s.IsFinal).Id), CancellationToken.None);

        await mediator.Send(new SprintCompleteCommand(Owner, sprint, next), CancellationToken.None);

        var moved = activities.ForTask(open).Last(a => a.Type == TaskActivityType.SprintChanged);
        Assert.Equal((sprint.ToString(), next.ToString()), (moved.OldValue, moved.NewValue));
        var report = (await mediator.Send(new SprintReportQuery(Owner, sprint), CancellationToken.None))!;
        Assert.Equal((2, 1, 1), (report.Committed.Count, report.Done.Count, report.NotDone.Count));
        // Спринт завершён в день старта: перенос незакрытой уже случился, но остаток на этот день — она же, а не ноль;
        // дальше до конца окна — только идеальная линия.
        Assert.Equal(4m, report.Burndown[0].Remaining);
        Assert.All(report.Burndown.Skip(1), p => Assert.Null(p.Remaining));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Plan(mediator, open, sprint));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new SprintUpdateCommand(Owner, sprint, Goal: "поздно"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Only_Planned_And_Returns_Tasks_To_Backlog()
    {
        var (mediator, _, tasks, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var planned = await SprintAsync(mediator, board);
        var active = await SprintAsync(mediator, board);
        var task = await TaskAsync(mediator, board, "Задача");
        await Plan(mediator, task, planned);
        await mediator.Send(new SprintStartCommand(Owner, active, Today, Today.AddDays(7)), CancellationToken.None);

        Assert.True(await mediator.Send(new SprintDeleteCommand(Owner, planned), CancellationToken.None));
        Assert.Null((await tasks.GetByIdAsync(task, CancellationToken.None))!.SprintId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new SprintDeleteCommand(Owner, active), CancellationToken.None));
    }

    [Fact]
    public async Task Rank_Moves_Between_Sections_And_Backlog_Shows_Sections_And_Epics()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var sprint = await SprintAsync(mediator, board, "Первый");
        var epic = await TaskAsync(mediator, board, "Эпик", SharedTypeKind.Epic);
        var inEpic = await TaskAsync(mediator, board, "Под эпиком", SharedTypeKind.Story, epic);
        var loose = await TaskAsync(mediator, board, "Сама по себе");
        await mediator.Send(new TaskSetEstimateCommand(Owner, inEpic, 5m, null), CancellationToken.None);

        var moved = await mediator.Send(new TaskRankCommand(Owner, inEpic, null, null, SprintId: sprint), CancellationToken.None);
        Assert.Equal(sprint, moved.Response!.SprintId);

        var backlog = (await mediator.Send(new BacklogQuery(Owner, board.Id), CancellationToken.None))!;
        Assert.Equal(["Первый", null], backlog.Sections.Select(s => s.Sprint?.Name));
        var sprintItem = Assert.Single(backlog.Sections[0].Items);
        Assert.Equal((inEpic, epic), (sprintItem.Task.Id, sprintItem.EpicId));
        Assert.Equal(5m, backlog.Sections[0].Points);
        Assert.Equal([loose], backlog.Sections[1].Items.Select(i => i.Task.Id));
        Assert.Equal(1, Assert.Single(backlog.Epics).Count);

        var byEpic = (await mediator.Send(new BacklogQuery(Owner, board.Id, EpicId: epic), CancellationToken.None))!;
        Assert.Empty(byEpic.Sections[1].Items);

        await mediator.Send(new TaskRankCommand(Owner, inEpic, null, null, ToBacklog: true), CancellationToken.None);
        Assert.Equal(2, (await mediator.Send(new BacklogQuery(Owner, board.Id), CancellationToken.None))!.Sections[1].Items.Count);
    }
}
