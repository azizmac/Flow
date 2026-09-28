using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusCreateCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Commands.StatusReorderCommand;
using Flow.Application.Features.Boards.Commands.StatusUpdateCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Статусы на реальном Postgres (docs/TZ_workflow_config.md §1, этап 3A): то, что зависит от SQL, —
/// перенос задач до DELETE статуса при FK Restrict и перестановка при unique (BoardId, SortOrder).
/// </summary>
[Collection(PostgresCollection.Name)]
public class StatusPersistenceTests(PostgresFixture db)
{
    private static Guid StatusOf(Shared.Contracts.Boards.BoardResponse board, string name) => board.Statuses.Single(s => s.Name == name).Id;

    [Fact]
    public async Task Delete_Should_Move_Tasks_Before_Removing_Status()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Status delete", "STD"))).Response!;
        var review = StatusOf(board, "На проверке");
        var done = StatusOf(board, "Сделана");
        var first = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "A", null, review)))!.Id;
        var second = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "B", null, review)))!.Id;

        var response = (await db.SendAsync(new StatusDeleteCommand(PostgresFixture.OwnerId, board.Id, review, done)))!;

        Assert.Equal(3, response.Statuses.Count);
        var statuses = await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.Id == first || t.Id == second).Select(t => t.StatusId).ToListAsync());
        Assert.All(statuses, s => Assert.Equal(done, s));
        Assert.False(await db.QueryAsync(ctx => ctx.Set<Status>().AnyAsync(s => s.Id == review)));
        Assert.Equal(2, await db.QueryAsync(ctx => ctx.TaskActivities.CountAsync(a =>
            (a.TaskId == first || a.TaskId == second) && a.Type == TaskActivityType.StatusChanged)));
    }

    [Fact]
    public async Task Reorder_Should_Survive_Unique_SortOrder_Repeatedly()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Status order", "STO"))).Response!;
        var added = (await db.SendAsync(new StatusCreateCommand(PostgresFixture.OwnerId, board.Id, "Блокирована", StatusType.InProgress)))!;
        var order = added.Statuses.Select(s => s.Id).Reverse().ToList();

        await db.SendAsync(new StatusReorderCommand(PostgresFixture.OwnerId, board.Id, order));
        order = [order[^1], .. order[..^1]];
        await db.SendAsync(new StatusReorderCommand(PostgresFixture.OwnerId, board.Id, order));

        var reloaded = (await db.SendAsync(new BoardGetQuery(PostgresFixture.OwnerId, board.Id)))!;
        Assert.Equal(order, reloaded.Statuses.Select(s => s.Id));
    }

    [Fact]
    public async Task Flags_And_Type_Should_Persist()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Status flags", "STF"))).Response!;
        var inWork = StatusOf(board, "В работе");
        var review = StatusOf(board, "На проверке");

        await db.SendAsync(new StatusUpdateCommand(PostgresFixture.OwnerId, board.Id, inWork, Name: "Разработка", IsInitial: true, ClearType: true));
        await db.SendAsync(new StatusUpdateCommand(PostgresFixture.OwnerId, board.Id, review, IsFinal: true));

        var reloaded = (await db.SendAsync(new BoardGetQuery(PostgresFixture.OwnerId, board.Id)))!;
        var renamed = reloaded.Statuses.Single(s => s.Id == inWork);
        Assert.Equal("Разработка", renamed.Name);
        Assert.True(renamed.IsInitial);
        Assert.Null(renamed.Type);
        Assert.Single(reloaded.Statuses, s => s.IsInitial);
        Assert.Equal(2, reloaded.Statuses.Count(s => s.IsFinal));
    }
}
