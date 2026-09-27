using Flow.Domain.Ranking;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Миграция AddTaskHierarchyAndRank на живой базе: существующие задачи получают ранги по времени создания
/// внутри проекта, ключи валидны для FractionalIndex, новая задача встаёт следом. Свой контейнер, как у
/// TaskTypesMigrationTests.
/// </summary>
public sealed class TaskRankMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260927165357_AddBoardVisibility";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
        .Options);

    [Fact]
    public async Task Existing_Tasks_Should_Get_Ranks_By_CreatedAt_Per_Board()
    {
        var (boardA, boardB, status, typeA, typeB) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (late, early, middle, other) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Boards" ("Id", "Key", "Name", "CreatedAt", "NextTaskNumber") VALUES
                    ({0}, 'RKA', 'A', '2026-09-01T10:00:00Z', 3), ({1}, 'RKB', 'B', '2026-09-01T10:00:00Z', 1);
                INSERT INTO "Statuses" ("Id", "BoardId", "Name", "SortOrder", "IsInitial", "IsFinal", "Type")
                    VALUES ({2}, {0}, 'Не начата', 0, true, false, 0);
                INSERT INTO "TaskTypes" ("Id", "BoardId", "Name", "Kind", "SortOrder", "IsDefault", "IsArchived") VALUES
                    ({3}, {0}, 'Задача', 2, 0, true, false), ({4}, {1}, 'Задача', 2, 0, true, false);
                """, boardA, boardB, status, typeA, typeB);

            // Статус второго проекта не нужен для рангов, но FK требует свой; вставляем задачи в «чужом» порядке.
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Statuses" ("Id", "BoardId", "Name", "SortOrder", "IsInitial", "IsFinal", "Type")
                    VALUES ({0}, {1}, 'Не начата', 0, true, false, 0);
                """, Guid.NewGuid(), boardB);
            var statusB = await db.Database.SqlQueryRaw<Guid>("""SELECT "Id" AS "Value" FROM "Statuses" WHERE "BoardId" = {0}""", boardB).SingleAsync();

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "TaskItems" ("Id", "BoardId", "Code", "Title", "StatusId", "TypeId", "Priority", "CreatedAt", "UpdatedAt") VALUES
                    ({0}, {4}, 'RKA-3', 'late',   {6}, {7}, 0, '2026-09-03T10:00:00Z', '2026-09-03T10:00:00Z'),
                    ({1}, {4}, 'RKA-1', 'early',  {6}, {7}, 0, '2026-09-01T10:00:00Z', '2026-09-01T10:00:00Z'),
                    ({2}, {4}, 'RKA-2', 'middle', {6}, {7}, 0, '2026-09-02T10:00:00Z', '2026-09-02T10:00:00Z'),
                    ({3}, {5}, 'RKB-1', 'other',  {8}, {9}, 0, '2026-09-05T10:00:00Z', '2026-09-05T10:00:00Z');
                """, late, early, middle, other, boardA, boardB, status, typeA, statusB, typeB);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            var ranks = await db.TaskItems.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Rank);

            Assert.Equal("d0001", ranks[early]);
            Assert.Equal("d0002", ranks[middle]);
            Assert.Equal("d0003", ranks[late]);
            Assert.Equal("d0001", ranks[other]);
            Assert.All(ranks.Values, r => Assert.True(FractionalIndex.IsValid(r), r));
            Assert.Equal("d0004", FractionalIndex.Between(ranks[late], null));
        }
    }
}
