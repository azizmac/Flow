using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;
using Flow.Application.Features.Boards.Commands.StatusCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Queries.TaskBoardQuery;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Flow.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Канбан на реальном Postgres (этап 2B): StatusChangedAt ставит UnitOfWork, окно финальной колонки считается по
/// DoneColumnDays своего проекта, колонка идёт по рангу, «Другие» сводного канбана — статусы без вида.
/// </summary>
[Collection(PostgresCollection.Name)]
public class KanbanPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Done_Column_Shows_Only_Tasks_Closed_Within_Project_Window()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Канбан", "KBN"))).Response!;
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        var fresh = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Свежая", null, null)))!.Id;
        var old = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Старая", null, null)))!.Id;

        await db.SendAsync(new TaskRankCommand(Owner, fresh, null, null, done));
        await db.SendAsync(new TaskRankCommand(Owner, old, null, null, done));

        // Смену статуса отметил UnitOfWork; старую закрытую «состариваем» на 30 дней.
        var stamped = await db.QueryAsync(ctx => ctx.TaskItems.AsNoTracking().SingleAsync(t => t.Id == fresh));
        Assert.True(stamped.StatusChangedAt > stamped.CreatedAt);
        await db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync(
            $"""UPDATE "TaskItems" SET "StatusChangedAt" = now() - interval '30 days' WHERE "Id" = {old}"""));

        var column = (await db.SendAsync(new TaskBoardQuery(Owner, board.Id)))!.Columns.Single(c => c.StatusId == done);
        Assert.Equal(1, column.Count);
        Assert.Equal(fresh, Assert.Single(column.Tasks).Id);

        await db.SendAsync(new BoardDoneColumnDaysSetCommand(Owner, board.Id, 60));
        column = (await db.SendAsync(new TaskBoardQuery(Owner, board.Id)))!.Columns.Single(c => c.StatusId == done);
        Assert.Equal(2, column.Count);
    }

    [Fact]
    public async Task Column_Is_Ordered_By_Rank_And_Moves_Keep_Order()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Порядок", "KBO"))).Response!;
        var doing = board.Statuses.Single(s => s.Name == "В работе").Id;
        var ids = new List<Guid>();
        foreach (var title in new[] { "A", "B", "C" })
            ids.Add((await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title, null, doing)))!.Id);

        // C — между A и B.
        await db.SendAsync(new TaskRankCommand(Owner, ids[2], ids[0], ids[1], doing));

        var column = (await db.SendAsync(new TaskBoardQuery(Owner, board.Id)))!.Columns.Single(c => c.StatusId == doing);
        Assert.Equal(["A", "C", "B"], column.Tasks.Select(t => t.Title));
    }

    [Fact]
    public async Task All_Projects_Other_Column_Holds_Untyped_Statuses()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Свои статусы", "KBU"))).Response!;
        var custom = (await db.SendAsync(new StatusCreateCommand(Owner, board.Id, "Ждёт заказчика", null, false)))!
            .Statuses.Single(s => s.Name == "Ждёт заказчика").Id;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Ждёт", null, custom)))!.Id;

        var other = (await db.SendAsync(new TaskBoardQuery(Owner, Other: true, Limit: 200)))!.Columns.Single();
        Assert.True(other.Other);
        Assert.Contains(task, other.Tasks.Select(t => t.Id));

        var typed = (await db.SendAsync(new TaskBoardQuery(Owner, Limit: 200)))!.Columns.Where(c => !c.Other);
        Assert.DoesNotContain(task, typed.SelectMany(c => c.Tasks).Select(t => t.Id));
    }
}

/// <summary>Миграция AddKanban: время смены статуса берётся из журнала, без записей — дата создания.</summary>
public sealed class KanbanMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260927182809_AddWorkflow";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
        .Options);

    [Fact]
    public async Task StatusChangedAt_Should_Come_From_Last_Status_Change_In_Journal()
    {
        var (board, status, type, user, moved, untouched) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using (var ctx = CreateContext())
        {
            await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await ctx.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Boards" ("Id", "Key", "Name", "CreatedAt", "NextTaskNumber") VALUES ({0}, 'KMG', 'K', '2026-09-01T10:00:00Z', 2);
                INSERT INTO "Statuses" ("Id", "BoardId", "Name", "SortOrder", "IsInitial", "IsFinal", "Type") VALUES ({1}, {0}, 'Не начата', 0, true, false, 0);
                INSERT INTO "TaskTypes" ("Id", "BoardId", "Name", "Kind", "SortOrder", "IsDefault", "IsArchived") VALUES ({2}, {0}, 'Задача', 2, 0, true, false);
                INSERT INTO "Users" ("Id", "Username", "Email", "FirstName", "LastName", "CreatedAt", "Role", "Status")
                    VALUES ({3}, 'kmg', 'kmg@example.com', 'K', 'M', '2026-09-01T10:00:00Z', 4, 1);
                INSERT INTO "TaskItems" ("Id", "BoardId", "Code", "Title", "StatusId", "TypeId", "Priority", "CreatedAt", "UpdatedAt", "Rank") VALUES
                    ({4}, {0}, 'KMG-1', 'moved', {1}, {2}, 0, '2026-09-02T10:00:00Z', '2026-09-20T10:00:00Z', 'd0001'),
                    ({5}, {0}, 'KMG-2', 'untouched', {1}, {2}, 0, '2026-09-03T10:00:00Z', '2026-09-21T10:00:00Z', 'd0002');
                INSERT INTO "TaskActivities" ("Id", "TaskId", "ActorId", "Type", "CreatedAt") VALUES
                    (gen_random_uuid(), {4}, {3}, 3, '2026-09-05T10:00:00Z'),
                    (gen_random_uuid(), {4}, {3}, 3, '2026-09-10T10:00:00Z'),
                    (gen_random_uuid(), {4}, {3}, 1, '2026-09-15T10:00:00Z');
                """, board, status, type, user, moved, untouched);

            await ctx.Database.MigrateAsync();
        }

        await using (var ctx = CreateContext())
        {
            var changed = await ctx.TaskItems.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.StatusChangedAt);
            Assert.Equal(new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc), changed[moved]);
            Assert.Equal(new DateTime(2026, 9, 3, 10, 0, 0, DateTimeKind.Utc), changed[untouched]);
            Assert.Equal(Board.DefaultDoneColumnDays, await ctx.Boards.Where(b => b.Id == board).Select(b => b.DoneColumnDays).SingleAsync());
        }
    }
}
