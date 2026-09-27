using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;
using DomainTypeKind = Flow.Domain.Entities.TaskTypeKind;
using SharedPriority = Flow.Shared.Contracts.Tasks.TaskPriority;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;
using TaskActivityType = Flow.Domain.Entities.TaskActivityType;
using TaskPriority = Flow.Domain.Entities.TaskPriority;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Этап 1A (docs/TZ_task_model.md): типы задач, приоритет, даты и оценки — поведение хендлеров: журнал на каждое
/// реально изменённое поле, отказы до записи в журнал и то, что индекс поиска эти поля не трогают.
/// Права — в PermissionTests, SQL (фильтры, FK, UpdatedAt) — в TaskPlanningPersistenceTests.
/// </summary>
public class TaskPlanningFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator, FakeBoardRepository boards, FakeTaskItemRepository tasks, string key = "FLW")
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Flow", key), CancellationToken.None)).Response!;
        tasks.RegisterBoardStatuses((await boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return board;
    }

    private static async Task<TaskResponse> CreateTaskAsync(IMediator mediator, Guid boardId) =>
        (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Task", null, null), CancellationToken.None))!;

    private static Guid TypeOf(BoardResponse board, SharedTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind).Id;

    // ---- создание ----

    [Fact]
    public async Task CreateTask_Should_UseDefaultTypeAndNoPriority()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);

        var task = await CreateTaskAsync(mediator, board.Id);

        Assert.Equal(board.TaskTypes.Single(t => t.IsDefault).Id, task.TypeId);
        Assert.Equal(SharedPriority.None, task.Priority);
        Assert.Equal(task.CreatedAt, task.UpdatedAt);
    }

    [Fact]
    public async Task CreateTask_Should_TakeTypeAndPriority_And_LogOnlyCreated()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var bug = TypeOf(board, SharedTypeKind.Bug);

        var task = (await mediator.Send(new TaskCreateCommand(Owner, board.Id, "Баг", null, null, bug, TaskPriority.High), CancellationToken.None))!;

        Assert.Equal(bug, task.TypeId);
        Assert.Equal(SharedPriority.High, task.Priority);
        Assert.Equal(TaskActivityType.Created, Assert.Single(activities.ForTask(task.Id)).Type);
    }

    [Fact]
    public async Task CreateTask_Should_RejectTypeOfAnotherBoard()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var other = await CreateBoardAsync(mediator, boards, tasks, "OTH");

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(
            new TaskCreateCommand(Owner, board.Id, "Чужой тип", null, null, TypeOf(other, SharedTypeKind.Bug)), CancellationToken.None));
    }

    // ---- тип и приоритет в TaskUpdate ----

    [Fact]
    public async Task Update_Should_ChangeTypeAndPriority_And_LogEach()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var story = TypeOf(board, SharedTypeKind.Story);

        var result = await mediator.Send(new TaskUpdateCommand(Owner, task.Id, null, null, null, story, TaskPriority.Critical), CancellationToken.None);

        Assert.Equal(story, result.Response!.TypeId);
        Assert.Equal(SharedPriority.Critical, result.Response.Priority);
        var log = activities.ForTask(task.Id).Where(a => a.Type != TaskActivityType.Created).ToList();
        var type = Assert.Single(log, a => a.Type == TaskActivityType.TypeChanged);
        Assert.Equal((task.TypeId.ToString(), story.ToString()), (type.OldValue, type.NewValue));
        var priority = Assert.Single(log, a => a.Type == TaskActivityType.PriorityChanged);
        Assert.Equal(("0", "4"), (priority.OldValue, priority.NewValue));
    }

    [Fact]
    public async Task Update_Should_NotLog_When_TypeAndPrioritySame()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, null, null, null, task.TypeId, TaskPriority.None), CancellationToken.None);

        Assert.Equal(TaskActivityType.Created, Assert.Single(activities.ForTask(task.Id)).Type);
    }

    [Fact]
    public async Task Update_Should_ReturnValidationError_When_TypeFromAnotherBoard()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var other = await CreateBoardAsync(mediator, boards, tasks, "OTH");
        var task = await CreateTaskAsync(mediator, board.Id);

        var result = await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Новое", null, null, TypeOf(other, SharedTypeKind.Bug)), CancellationToken.None);

        Assert.NotNull(result.ValidationError);
        // Отказ — до любых изменений: название осталось, в журнале только создание.
        Assert.Equal("Task", (await tasks.GetByIdAsync(task.Id, CancellationToken.None))!.Title);
        Assert.Single(activities.ForTask(task.Id));
    }

    [Fact]
    public async Task Update_Should_Throw_When_TypeArchived()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var epic = TypeOf(board, SharedTypeKind.Epic);
        await mediator.Send(new TaskTypeUpdateCommand(Owner, board.Id, epic, IsArchived: true), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskUpdateCommand(Owner, task.Id, null, null, null, epic), CancellationToken.None));
    }

    // ---- даты ----

    [Fact]
    public async Task SetSchedule_Should_LogStartAndDueSeparately()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        var result = await mediator.Send(new TaskSetScheduleCommand(Owner, task.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 5)), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 10, 1), result.Response!.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 5), result.Response.DueDate);
        var log = activities.ForTask(task.Id);
        Assert.Equal("2026-10-01", Assert.Single(log, a => a.Type == TaskActivityType.StartDateChanged).NewValue);
        Assert.Equal("2026-10-05", Assert.Single(log, a => a.Type == TaskActivityType.DueDateChanged).NewValue);

        // Сдвиг только срока — одна запись.
        await mediator.Send(new TaskSetScheduleCommand(Owner, task.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7)), CancellationToken.None);
        Assert.Single(activities.ForTask(task.Id), a => a.Type == TaskActivityType.StartDateChanged);
        Assert.Equal(2, activities.ForTask(task.Id).Count(a => a.Type == TaskActivityType.DueDateChanged));
    }

    [Fact]
    public async Task SetSchedule_Should_Throw_And_NotLog_When_StartAfterDue()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(
            new TaskSetScheduleCommand(Owner, task.Id, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1)), CancellationToken.None));

        Assert.Single(activities.ForTask(task.Id));
    }

    [Fact]
    public async Task SetDueDate_Should_Throw_When_EarlierThanStart()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        await mediator.Send(new TaskSetScheduleCommand(Owner, task.Id, new DateOnly(2026, 10, 5), null), CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, new DateOnly(2026, 10, 1)), CancellationToken.None));

        Assert.DoesNotContain(activities.ForTask(task.Id), a => a.Type == TaskActivityType.DueDateChanged);
    }

    // ---- оценки ----

    [Fact]
    public async Task SetEstimate_Should_LogEachChangedValue()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        var result = await mediator.Send(new TaskSetEstimateCommand(Owner, task.Id, 5m, 240), CancellationToken.None);

        Assert.Equal(5m, result.Response!.StoryPoints);
        Assert.Equal(240, result.Response.EstimateMinutes);
        Assert.Equal("5", Assert.Single(activities.ForTask(task.Id), a => a.Type == TaskActivityType.StoryPointsChanged).NewValue);
        Assert.Equal("240", Assert.Single(activities.ForTask(task.Id), a => a.Type == TaskActivityType.EstimateChanged).NewValue);

        await mediator.Send(new TaskSetEstimateCommand(Owner, task.Id, 5m, null), CancellationToken.None);
        Assert.Single(activities.ForTask(task.Id), a => a.Type == TaskActivityType.StoryPointsChanged);
        Assert.Equal(2, activities.ForTask(task.Id).Count(a => a.Type == TaskActivityType.EstimateChanged));
    }

    [Fact]
    public async Task SetEstimate_Should_NotLogFirstValue_When_SecondInvalid()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new TaskSetEstimateCommand(Owner, task.Id, 3m, -5), CancellationToken.None));

        Assert.Single(activities.ForTask(task.Id));
    }

    [Fact]
    public async Task PlanningCommands_Should_ReturnNotFound_ForUnknownTask()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        Assert.True((await mediator.Send(new TaskSetScheduleCommand(Owner, Guid.NewGuid(), null, null), CancellationToken.None)).IsNotFound);
        Assert.True((await mediator.Send(new TaskSetEstimateCommand(Owner, Guid.NewGuid(), null, null), CancellationToken.None)).IsNotFound);
    }

    [Fact]
    public async Task PlanningFields_Should_NotTouchSearchIndex()
    {
        var (mediator, boards, tasks, _, _, searchIndex) = TestMediatorFactory.CreateWithSearchIndex();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        searchIndex.Clear();

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, null, null, null, TypeOf(board, SharedTypeKind.Bug), TaskPriority.High), CancellationToken.None);
        await mediator.Send(new TaskSetScheduleCommand(Owner, task.Id, new DateOnly(2026, 10, 1), null), CancellationToken.None);
        await mediator.Send(new TaskSetEstimateCommand(Owner, task.Id, 2m, 60), CancellationToken.None);

        Assert.Empty(searchIndex.All);
    }

    // ---- типы проекта ----

    [Fact]
    public async Task TaskTypeCreate_Should_AddType_And_ReturnBoard()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);

        var updated = (await mediator.Send(new TaskTypeCreateCommand(Owner, board.Id, "Инцидент", DomainTypeKind.Bug, IsDefault: true), CancellationToken.None))!;

        var created = updated.TaskTypes.Last();
        Assert.Equal(("Инцидент", SharedTypeKind.Bug, 3, true), (created.Name, created.Kind, created.Level, created.IsDefault));
        Assert.Single(updated.TaskTypes, t => t.IsDefault);
    }

    [Fact]
    public async Task TaskTypeCommands_Should_ReturnNull_ForUnknownBoard()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        Assert.Null(await mediator.Send(new TaskTypeCreateCommand(Owner, Guid.NewGuid(), "X", DomainTypeKind.Task), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskTypeUpdateCommand(Owner, Guid.NewGuid(), Guid.NewGuid(), Name: "X"), CancellationToken.None));
    }

    [Fact]
    public async Task TaskTypeUpdate_Should_RestoreAndMakeDefault_InOneCall()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var epic = TypeOf(board, SharedTypeKind.Epic);
        await mediator.Send(new TaskTypeUpdateCommand(Owner, board.Id, epic, IsArchived: true), CancellationToken.None);

        var updated = (await mediator.Send(new TaskTypeUpdateCommand(Owner, board.Id, epic, IsDefault: true, IsArchived: false), CancellationToken.None))!;

        var type = updated.TaskTypes.Single(t => t.Id == epic);
        Assert.True(type.IsDefault);
        Assert.False(type.IsArchived);
    }

    [Fact]
    public async Task TaskTypeUpdate_Should_RejectClearingDefault_And_ArchivingDefault()
    {
        var (mediator, boards, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var @default = board.TaskTypes.Single(t => t.IsDefault).Id;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new TaskTypeUpdateCommand(Owner, board.Id, @default, IsDefault: false), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskTypeUpdateCommand(Owner, board.Id, @default, IsArchived: true), CancellationToken.None));
    }
}
