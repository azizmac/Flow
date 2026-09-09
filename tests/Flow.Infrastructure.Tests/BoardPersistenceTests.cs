using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public class BoardPersistenceTests(PostgresFixture db)
{
    [Fact]
    public async Task Migrations_Should_BeFullyApplied()
    {
        var pending = await db.QueryAsync(ctx => ctx.Database.GetPendingMigrationsAsync());

        Assert.Empty(pending);
    }

    [Fact]
    public async Task DeleteBoard_Should_RemoveBoardStatusesAndTasks_When_BoardHasTasks()
    {
        // Регрессия: у TaskItems FK на Statuses с Restrict; без загрузки задач в трекер
        // EF удалял статусы первыми и Postgres отвечал 23503.
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Delete me", "DEL"))).Response!;
        var doneStatusId = board.Statuses.Single(s => s.IsFinal).Id;
        var task1 = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Task 1", null, null)))!;
        var task2 = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Task 2", "desc", doneStatusId)))!;
        await db.SendAsync(new TaskUpdateCommand(PostgresFixture.OwnerId, task1.Id, null, null, doneStatusId));

        var deleted = await db.SendAsync(new BoardDeleteCommand(PostgresFixture.OwnerId, board.Id));

        Assert.True(deleted);
        Assert.Null(await db.SendAsync(new BoardGetQuery(board.Id)));
        Assert.Null(await db.SendAsync(new TaskGetQuery(task1.Id)));
        Assert.Null(await db.SendAsync(new TaskGetQuery(task2.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.Statuses.CountAsync(s => s.BoardId == board.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.TaskItems.CountAsync(t => t.BoardId == board.Id)));
    }

    [Fact]
    public async Task DeleteBoard_Should_NotTouchOtherBoards()
    {
        var keep = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Keep", "KEEP"))).Response!;
        var keepTask = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, keep.Id, "Stays", null, null)))!;
        var drop = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Drop", "DROP"))).Response!;
        await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, drop.Id, "Goes", null, null));

        await db.SendAsync(new BoardDeleteCommand(PostgresFixture.OwnerId, drop.Id));

        Assert.NotNull(await db.SendAsync(new BoardGetQuery(keep.Id)));
        Assert.NotNull(await db.SendAsync(new TaskGetQuery(keepTask.Id)));
        Assert.Equal(4, await db.QueryAsync(ctx => ctx.Statuses.CountAsync(s => s.BoardId == keep.Id)));
    }

    [Fact]
    public async Task CreateBoard_Should_ReturnKeyTaken_When_KeyAlreadyExists()
    {
        // Регрессия: раньше дубликат ключа долетал до IX_Boards_Key и превращался в 500.
        var first = await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "First", "DUP"));
        Assert.False(first.IsKeyTaken);

        var second = await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Second", "dup"));

        Assert.True(second.IsKeyTaken);
        Assert.Null(second.Response);
        Assert.Equal(1, await db.QueryAsync(ctx => ctx.Boards.CountAsync(b => b.Key == "DUP")));
    }

    [Fact]
    public async Task CreateTask_Should_PersistCodeAndStatus()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Persist", "PRS"))).Response!;
        var initialStatusId = board.Statuses.Single(s => s.IsInitial).Id;

        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Persisted", "text", null)))!;

        var stored = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.Equal("PRS-1", stored.Code.Value);
        Assert.Equal(initialStatusId, stored.StatusId);
        Assert.Equal("text", stored.Description);
    }

    [Fact]
    public async Task UpdateTask_Should_RejectStatusFromAnotherBoard()
    {
        var boardA = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "A", "STA"))).Response!;
        var boardB = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "B", "STB"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, boardA.Id, "Task", null, null)))!;
        var foreignStatusId = boardB.Statuses.Single(s => s.IsFinal).Id;

        var result = await db.SendAsync(new TaskUpdateCommand(PostgresFixture.OwnerId, task.Id, null, null, foreignStatusId));

        Assert.NotNull(result.ValidationError);
        var stored = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.Equal(task.StatusId, stored.StatusId);
    }
}
