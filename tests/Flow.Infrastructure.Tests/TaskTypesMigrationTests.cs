using Flow.Domain.Entities;
using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Миграция AddTaskTypesAndPlanning на живой базе. Генератор предлагал TypeId = пустой Guid и
/// UpdatedAt = 0001-01-01; Up переписан руками: каждому проекту — набор DefaultTaskTypes, задачам — тип
/// по умолчанию, UpdatedAt = CreatedAt. Тест держит эту правку. Свой контейнер, как у PreferencesMigrationTests.
/// </summary>
public sealed class TaskTypesMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260923174516_AddUserPreferences";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
        .Options);

    [Fact]
    public async Task Existing_Boards_And_Tasks_Should_Get_DefaultTypes()
    {
        var boardId = Guid.NewGuid();
        var statusId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var emptyBoardId = Guid.NewGuid();

        await using (var db = CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Boards" ("Id", "Key", "Name", "CreatedAt", "NextTaskNumber")
                VALUES ({0}, 'OLD', 'Old', '2026-09-01T10:00:00Z', 1), ({3}, 'EMP', 'Empty', '2026-09-01T10:00:00Z', 0);
                INSERT INTO "Statuses" ("Id", "BoardId", "Name", "SortOrder", "IsInitial", "IsFinal", "Type")
                VALUES ({1}, {0}, 'Не начата', 0, true, false, 0);
                INSERT INTO "TaskItems" ("Id", "BoardId", "Code", "Title", "StatusId", "CreatedAt")
                VALUES ({2}, {0}, 'OLD-1', 'Старая', {1}, '2026-09-02T10:00:00Z');
                """, boardId, statusId, taskId, emptyBoardId);

            await db.Database.MigrateAsync();
        }

        await using (var db = CreateContext())
        {
            foreach (var id in new[] { boardId, emptyBoardId })
            {
                var types = await db.TaskTypes.AsNoTracking().Where(t => t.BoardId == id).OrderBy(t => t.SortOrder).ToListAsync();
                Assert.Equal(DefaultTaskTypes.All.Select(d => (d.Name, d.Kind, d.IsDefault)), types.Select(t => (t.Name, t.Kind, t.IsDefault)));
            }

            var task = await db.TaskItems.AsNoTracking().SingleAsync(t => t.Id == taskId);
            var taskType = await db.TaskTypes.AsNoTracking().SingleAsync(t => t.Id == task.TypeId);
            Assert.Equal(boardId, taskType.BoardId);
            Assert.True(taskType.IsDefault);
            Assert.Equal(task.CreatedAt, task.UpdatedAt);
            Assert.Equal(TaskPriority.None, task.Priority);
        }
    }
}
