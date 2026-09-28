using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Milestones;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using SharedState = Flow.Shared.Contracts.Milestones.MilestoneState;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Вехи в Application (docs/TZ_task_views.md §6): имя уникально в проекте, прогресс и прогноз, закрытие с открытыми
/// задачами, удаление с журналом, поле вехи у задачи. Права — PermissionTests, FQL — FqlTests, SQL — Flow.Infrastructure.Tests.
/// </summary>
public class MilestoneFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<BoardResponse> BoardAsync(IMediator mediator, string key = "PRJ") =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект " + key, key), CancellationToken.None)).Response!;

    private static async Task<Guid> TaskAsync(IMediator mediator, BoardResponse board, string title) =>
        (await mediator.Send(new TaskCreateCommand(Owner, board.Id, title, null, null), CancellationToken.None))!.Id;

    private static async Task<Guid> MilestoneAsync(IMediator mediator, BoardResponse board, string name) =>
        (await mediator.Send(new MilestoneCreateCommand(Owner, board.Id, name), CancellationToken.None))!.Id;

    private static Task<TaskUpdateResult> Put(IMediator mediator, Guid task, Guid? milestone) =>
        mediator.Send(new TaskSetMilestoneCommand(Owner, task, milestone), CancellationToken.None);

    [Fact]
    public async Task Name_Is_Unique_In_Project_Ignoring_Case()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var other = await BoardAsync(mediator, "OTH");
        await MilestoneAsync(mediator, board, "Релиз 1.0");

        await Assert.ThrowsAsync<InvalidOperationException>(() => MilestoneAsync(mediator, board, "релиз 1.0"));
        Assert.NotEqual(Guid.Empty, await MilestoneAsync(mediator, other, "Релиз 1.0"));
    }

    [Fact]
    public async Task Progress_Counts_Done_Points_Overdue_And_Forecast()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var milestone = await MilestoneAsync(mediator, board, "1.0");
        var done = await TaskAsync(mediator, board, "Сделана");
        var working = await TaskAsync(mediator, board, "В работе");
        var late = await TaskAsync(mediator, board, "Просрочена");
        foreach (var (task, points) in new[] { (done, 3m), (working, 5m), (late, 2m) })
        {
            await mediator.Send(new TaskSetEstimateCommand(Owner, task, points, null), CancellationToken.None);
            await Put(mediator, task, milestone);
        }
        await mediator.Send(new TaskUpdateCommand(Owner, done, null, null, board.Statuses.Single(s => s.IsFinal).Id), CancellationToken.None);
        await mediator.Send(new TaskUpdateCommand(Owner, working, null, null, board.Statuses.Single(s => s.Name == "В работе").Id), CancellationToken.None);
        await mediator.Send(new TaskSetDueDateCommand(Owner, late, Today.AddDays(-1)), CancellationToken.None);

        var progress = (await mediator.Send(new MilestoneGetQuery(Owner, milestone), CancellationToken.None))!.Progress;

        Assert.Equal((3, 1, 1, 10m, 3m, 1), (progress.Total, progress.Done, progress.InProgress, progress.Points, progress.DonePoints, progress.Overdue));
        // Одна задача закрыта за 14 дней → две оставшиеся займут 28 дней.
        Assert.Equal(Today.AddDays(28), progress.Forecast);
    }

    [Fact]
    public async Task Closed_Milestone_Keeps_Tasks_Rejects_New_And_Reopens()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await BoardAsync(mediator);
        var milestone = await MilestoneAsync(mediator, board, "1.0");
        var inside = await TaskAsync(mediator, board, "Внутри");
        var outside = await TaskAsync(mediator, board, "Снаружи");
        await Put(mediator, inside, milestone);

        var closed = (await mediator.Send(new MilestoneUpdateCommand(Owner, milestone, Closed: true), CancellationToken.None))!;
        Assert.Equal((SharedState.Closed, 1), (closed.State, closed.Progress.Total));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Put(mediator, outside, milestone));

        await mediator.Send(new MilestoneUpdateCommand(Owner, milestone, Closed: false), CancellationToken.None);
        Assert.Equal(milestone, (await Put(mediator, outside, milestone)).Response!.MilestoneId);
    }

    [Fact]
    public async Task Delete_Clears_Tasks_With_Journal_And_Foreign_Milestone_Is_Rejected()
    {
        var (mediator, _, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await BoardAsync(mediator);
        var other = await BoardAsync(mediator, "OTH");
        var milestone = await MilestoneAsync(mediator, board, "1.0");
        var foreign = await MilestoneAsync(mediator, other, "2.0");
        var task = await TaskAsync(mediator, board, "Задача");
        await Put(mediator, task, milestone);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Put(mediator, task, foreign));
        Assert.True(await mediator.Send(new MilestoneDeleteCommand(Owner, milestone), CancellationToken.None));

        Assert.Null((await tasks.GetByIdAsync(task, CancellationToken.None))!.MilestoneId);
        var entries = activities.ForTask(task).Where(a => a.Type == TaskActivityType.MilestoneChanged).ToList();
        Assert.Equal([(null, milestone.ToString()), (milestone.ToString(), null)], entries.Select(a => (a.OldValue, a.NewValue)));
        Assert.Null(await mediator.Send(new MilestoneGetQuery(Owner, milestone), CancellationToken.None));
    }

    /// <summary>Общая веха (этап 2H): доступна в проектах-участниках, прогресс общий, из убранного проекта задачи выходят с журналом.</summary>
    [Fact]
    public async Task Shared_Milestone_Takes_Tasks_Of_Other_Projects()
    {
        var (mediator, _, tasks, _) = TestMediatorFactory.Create();
        var front = await BoardAsync(mediator, "FRONT");
        var back = await BoardAsync(mediator, "BACK");
        var release = await MilestoneAsync(mediator, front, "Релиз 2.0");
        var backTask = await TaskAsync(mediator, back, "API");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Put(mediator, backTask, release));

        var shared = (await mediator.Send(new MilestoneShareCommand(Owner, release, [back.Id, front.Id, back.Id]), CancellationToken.None))!;
        Assert.Equal([back.Id], shared.SharedBoardIds);
        Assert.NotNull((await Put(mediator, backTask, release)).Response);
        await Put(mediator, await TaskAsync(mediator, front, "Экран"), release);

        var inBack = Assert.Single((await mediator.Send(new MilestoneListQuery(Owner, back.Id), CancellationToken.None))!);
        Assert.Equal((release, front.Id, 2), (inBack.Id, inBack.BoardId, inBack.Progress.Total));

        // В проекте уже есть веха с тем же именем — общей её там не сделать: FQL ищет вехи по имени.
        var ops = await BoardAsync(mediator, "OPS");
        await MilestoneAsync(mediator, ops, "релиз 2.0");
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new MilestoneShareCommand(Owner, release, [back.Id, ops.Id]), CancellationToken.None));

        await mediator.Send(new MilestoneShareCommand(Owner, release, []), CancellationToken.None);
        Assert.Null((await tasks.GetByIdAsync(backTask, CancellationToken.None))!.MilestoneId);
        Assert.Empty((await mediator.Send(new MilestoneListQuery(Owner, back.Id), CancellationToken.None))!);
    }
}
