using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Миграция AddSearchIndex: применяется на чистой базе и откатывается. Свой контейнер, а не общая
/// коллекция: в ней база уже доведена до последней версии, и откатывать её под другими тестами нельзя.
/// </summary>
public sealed class SearchMigrationTests : IAsyncLifetime
{
    /// <summary>Последняя миграция до поискового индекса — на неё откатываемся.</summary>
    private const string PreviousMigration = "20260911070626_AddTaskCommentsAndActivity";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), o => o.UseVector())
        .Options);

    [Fact]
    public async Task AddSearchIndex_Applies_And_Rolls_Back()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        Assert.True(await TableExistsAsync(db, "SearchChunks"));
        Assert.True(await TableExistsAsync(db, "SearchIndexQueue"));
        Assert.Equal(1, await ExtensionCountAsync(db));

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.False(await TableExistsAsync(db, "SearchChunks"));
        Assert.False(await TableExistsAsync(db, "SearchIndexQueue"));
        // Расширение откат не трогает: его могли поставить руками или использовать другие схемы.
        Assert.Equal(1, await ExtensionCountAsync(db));

        // Данные прежних миграций на месте — откат уносит только то, что добавил поиск.
        Assert.True(await TableExistsAsync(db, "TaskComments"));
    }

    private static Task<bool> TableExistsAsync(FlowDbContext db, string table) =>
        db.Database
            .SqlQueryRaw<bool>("SELECT to_regclass({0}) IS NOT NULL AS \"Value\"", $"public.\"{table}\"")
            .SingleAsync();

    private static Task<int> ExtensionCountAsync(FlowDbContext db) =>
        db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_extension WHERE extname = 'vector'")
            .SingleAsync();
}
