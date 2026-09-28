using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Связи и чек-лист на реальном Postgres (этап 1C): owned-коллекция пунктов (INSERT, а не UPDATE, у сохранённой
/// задачи; UpdatedAt), счётчик блокировок через join статусов, unique и каскады связей — в том числе между проектами.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskLinkChecklistPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private async Task<BoardResponse> CreateBoardAsync(string key) =>
        (await db.SendAsync(new BoardCreateCommand(Owner, $"Board {key}", key))).Response!;

    private async Task<Guid> CreateTaskAsync(BoardResponse board, string title = "T") =>
        (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, null)))!.Id;

    [Fact]
    public async Task Checklist_Should_Persist_And_Touch_UpdatedAt()
    {
        var board = await CreateBoardAsync("LCK");
        var task = await CreateTaskAsync(board);
        var before = (await db.SendAsync(new TaskGetQuery(Owner, task)))!.UpdatedAt;

        await db.SendAsync(new TaskChecklistAddCommand(Owner, task, "Первый"));
        var items = (await db.SendAsync(new TaskChecklistAddCommand(Owner, task, "Второй")))!;
        await db.SendAsync(new TaskChecklistUpdateCommand(Owner, task, items[0].Id, IsDone: true));
        await db.SendAsync(new TaskChecklistReorderCommand(Owner, task, [items[1].Id, items[0].Id]));

        var reloaded = (await db.SendAsync(new TaskGetQuery(Owner, task)))!;
        Assert.Equal((1, 2), (reloaded.ChecklistDone, reloaded.ChecklistTotal));
        Assert.True(reloaded.UpdatedAt > before);

        var stored = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task));
        Assert.Equal(["Второй", "Первый"], stored.Checklist.Select(i => i.Text));
        Assert.True(stored.Checklist[1].IsDone);

        // Список задач со страницей и сортировкой по подзапросу не ломается из-за owned-коллекции.
        var page = await db.SendAsync(new TaskSearchQuery(Owner, board.Id, Offset: 0, Sort: TaskSortField.Status));
        Assert.Equal(2, page.Items.Single().ChecklistTotal);
    }

    [Fact]
    public async Task Blockers_Unique_And_Cascades_Should_Come_From_Sql()
    {
        var board = await CreateBoardAsync("LBK");
        var other = await CreateBoardAsync("LBO");
        var blocker = await CreateTaskAsync(board, "Блокер");
        var blocked = await CreateTaskAsync(board, "Ждёт");
        var foreign = await CreateTaskAsync(other, "В другом проекте");

        await db.SendAsync(new TaskLinkCreateCommand(Owner, blocker, TaskLinkType.Blocks, blocked));
        await db.SendAsync(new TaskLinkCreateCommand(Owner, foreign, TaskLinkType.Blocks, blocked));
        await db.SendAsync(new TaskLinkCreateCommand(Owner, blocked, TaskLinkType.RelatesTo, foreign));
        Assert.Equal(2, (await db.SendAsync(new TaskGetQuery(Owner, blocked)))!.BlockedByCount);

        await db.SendAsync(new TaskUpdateCommand(Owner, blocker, null, null, board.Statuses.Single(s => s.IsFinal).Id));
        Assert.Equal(1, (await db.SendAsync(new TaskGetQuery(Owner, blocked)))!.BlockedByCount);

        var links = (await db.SendAsync(new TaskLinkListQuery(Owner, blocked)))!;
        Assert.Equal(3, links.Count);
        Assert.True(links.Single(l => l.Other.Id == blocker).Other.IsDone);

        // Удаление задачи и чужого проекта уносит связи каскадом БД.
        Assert.True(await db.SendAsync(new TaskDeleteCommand(Owner, blocker)));
        Assert.True(await db.SendAsync(new BoardDeleteCommand(Owner, other.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.TaskLinks.CountAsync(l => l.SourceTaskId == blocked || l.TargetTaskId == blocked)));
    }

    [Fact]
    public async Task Same_Link_Twice_Should_Hit_Unique_Index()
    {
        var board = await CreateBoardAsync("LUQ");
        var a = await CreateTaskAsync(board, "A");
        var b = await CreateTaskAsync(board, "B");

        await Assert.ThrowsAsync<DbUpdateException>(() => db.InScopeAsync(async sp =>
        {
            var ctx = sp.GetRequiredService<FlowDbContext>();
            ctx.TaskLinks.Add(TaskLink.Create(a, b, TaskLinkType.Duplicates, Owner));
            ctx.TaskLinks.Add(TaskLink.Create(a, b, TaskLinkType.Duplicates, Owner));
            await ctx.SaveChangesAsync();
        }));
    }
}
