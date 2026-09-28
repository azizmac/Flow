using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;
using DomainPriority = Flow.Domain.Entities.TaskPriority;
using DomainTypeKind = Flow.Domain.Entities.TaskTypeKind;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Типы задач, приоритет, даты и оценки на реальном Postgres (docs/TZ_task_model.md, этап 1A):
/// то, что зависит от SQL — FK Restrict на типы при удалении проекта, numeric(5,1), фильтр по виду через подзапрос
/// и UpdatedAt, который ставит UnitOfWork, а не хендлер.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskPlanningPersistenceTests(PostgresFixture db)
{
    [Fact]
    public async Task CreateBoard_Should_PersistDefaultTaskTypes_And_CreateTaskOnDefault()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Types seed", "TTS"))).Response!;

        var reloaded = (await db.SendAsync(new BoardGetQuery(PostgresFixture.OwnerId, board.Id)))!;
        Assert.Equal(5, reloaded.TaskTypes.Count);
        var @default = Assert.Single(reloaded.TaskTypes, t => t.IsDefault);
        Assert.Equal(SharedTypeKind.Task, @default.Kind);

        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "A", null, null)))!;
        Assert.Equal(@default.Id, task.TypeId);
    }

    [Fact]
    public async Task TaskTypeCommands_Should_Persist()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Types edit", "TTE"))).Response!;
        var epic = board.TaskTypes.Single(t => t.Kind == SharedTypeKind.Epic);

        var afterCreate = (await db.SendAsync(new TaskTypeCreateCommand(PostgresFixture.OwnerId, board.Id, "Инцидент", DomainTypeKind.Bug, IsDefault: true)))!;
        var incident = afterCreate.TaskTypes.Single(t => t.Name == "Инцидент");
        await db.SendAsync(new TaskTypeUpdateCommand(PostgresFixture.OwnerId, board.Id, epic.Id, Name: "Большая цель", IsArchived: true));

        var reloaded = (await db.SendAsync(new BoardGetQuery(PostgresFixture.OwnerId, board.Id)))!;
        Assert.Equal(incident.Id, Assert.Single(reloaded.TaskTypes, t => t.IsDefault).Id);
        var renamed = reloaded.TaskTypes.Single(t => t.Id == epic.Id);
        Assert.Equal("Большая цель", renamed.Name);
        Assert.True(renamed.IsArchived);
        Assert.Equal(reloaded.TaskTypes.Count - 1, reloaded.TaskTypes.ToList().FindIndex(t => t.Id == incident.Id));
    }

    [Fact]
    public async Task PlanningFields_Should_RoundTrip()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Planning", "PLN"))).Response!;
        var bug = board.TaskTypes.Single(t => t.Kind == SharedTypeKind.Bug);
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Баг", null, null, bug.Id, DomainPriority.High)))!;

        await db.SendAsync(new TaskSetScheduleCommand(PostgresFixture.OwnerId, task.Id, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)));
        await db.SendAsync(new TaskSetEstimateCommand(PostgresFixture.OwnerId, task.Id, 3.5m, 150));

        var reloaded = (await db.SendAsync(new TaskGetQuery(PostgresFixture.OwnerId, task.Id)))!;
        Assert.Equal(bug.Id, reloaded.TypeId);
        Assert.Equal(TaskPriority.High, reloaded.Priority);
        Assert.Equal(new DateOnly(2026, 10, 1), reloaded.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 3), reloaded.DueDate);
        Assert.Equal(3.5m, reloaded.StoryPoints);
        Assert.Equal(150, reloaded.EstimateMinutes);
    }

    [Fact]
    public async Task UpdatedAt_Should_MoveOnTaskChange_ButNotOnComment()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Updated at", "UPD"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Старое", null, null)))!;
        Assert.Equal(task.CreatedAt, task.UpdatedAt);

        await db.SendAsync(new TaskCommentAddCommand(PostgresFixture.OwnerId, task.Id, "Комментарий"));
        var afterComment = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.Equal(afterComment.CreatedAt, afterComment.UpdatedAt);

        await db.SendAsync(new TaskUpdateCommand(PostgresFixture.OwnerId, task.Id, "Новое", null, null));
        var afterRename = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.True(afterRename.UpdatedAt > afterRename.CreatedAt);
    }

    [Fact]
    public async Task Search_Should_FilterByTypeKindAndPriority_And_SortByPriority()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Kinds", "KND"))).Response!;
        var bugType = board.TaskTypes.Single(t => t.Kind == SharedTypeKind.Bug);
        var low = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Низкий баг", null, null, bugType.Id, DomainPriority.Low)))!;
        var critical = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Срочный баг", null, null, bugType.Id, DomainPriority.Critical)))!;
        var plain = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Обычная", null, null, null, DomainPriority.Critical)))!;

        var bugs = await db.SendAsync(new TaskSearchQuery(PostgresFixture.OwnerId, BoardId: board.Id, TypeKind: SharedTypeKind.Bug, Offset: 0,
            Sort: TaskSortField.Priority, Descending: true));
        Assert.Equal([critical.Id, low.Id], bugs.Items.Select(t => t.Id));
        Assert.Equal(2, bugs.Matched);

        var criticalOnly = await db.SendAsync(new TaskSearchQuery(PostgresFixture.OwnerId, BoardId: board.Id, Priority: TaskPriority.Critical));
        Assert.Equal(new HashSet<Guid> { critical.Id, plain.Id }, criticalOnly.Items.Select(t => t.Id).ToHashSet());
    }

    [Fact]
    public async Task DeleteBoard_Should_RemoveTaskTypes_When_TasksUseSeveralTypes()
    {
        // FK TaskItems.TypeId — Restrict, как у статусов: без загрузки типов вместе с доской EF удалил бы их
        // раньше задач, и Postgres ответил бы 23503.
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Delete types", "DLT"))).Response!;
        foreach (var type in board.TaskTypes)
            await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, type.Name, null, null, type.Id));

        Assert.True(await db.SendAsync(new BoardDeleteCommand(PostgresFixture.OwnerId, board.Id)));

        Assert.Equal(0, await db.QueryAsync(ctx => ctx.TaskTypes.CountAsync(t => t.BoardId == board.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.TaskItems.CountAsync(t => t.BoardId == board.Id)));
    }
}
