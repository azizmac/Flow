using Flow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Миграция AddSearchIndex накатывается и откатывается на чистой базе. Свой контейнер, а не общая
/// коллекция: та уже доведена до последней версии, а здесь нужен шаг «до» и «после».
/// </summary>
public sealed class SearchMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20260911070626_AddTaskCommentsAndActivity";
    private const string SearchMigration = "20260915120000_AddSearchIndex";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private FlowDbContext CreateContext() => new(new DbContextOptionsBuilder<FlowDbContext>()
        .UseNpgsql(_container.GetConnectionString(), npgsql => npgsql.UseVector())
        .Options);

    [Fact]
    public async Task AddSearchIndex_Should_Apply_And_Roll_Back()
    {
        await using var db = CreateContext();
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync(PreviousMigration);
        Assert.False(await TableExistsAsync(db, "SearchChunks"));

        await migrator.MigrateAsync(SearchMigration);

        Assert.True(await TableExistsAsync(db, "SearchChunks"));
        Assert.True(await TableExistsAsync(db, "SearchIndexQueue"));
        Assert.True(await ExtensionExistsAsync(db, "vector"));
        // HNSW и GIN созданы сырым SQL — проверяем, что они действительно есть.
        Assert.True(await IndexExistsAsync(db, "IX_SearchChunks_Embedding_Hnsw"));
        Assert.True(await IndexExistsAsync(db, "IX_SearchChunks_Tsv_Gin"));

        await migrator.MigrateAsync(PreviousMigration);

        Assert.False(await TableExistsAsync(db, "SearchChunks"));
        Assert.False(await TableExistsAsync(db, "SearchIndexQueue"));
        // Расширение переживает откат намеренно: его могли включить до нас и им могут пользоваться другие схемы.
        Assert.True(await ExtensionExistsAsync(db, "vector"));

        // Данные соседних таблиц откат не трогает: миграция ничего, кроме своих таблиц, не создавала.
        Assert.True(await TableExistsAsync(db, "TaskComments"));
    }

    private static Task<bool> TableExistsAsync(FlowDbContext db, string table) =>
        ScalarAsync(db, $"SELECT to_regclass('public.\"{table}\"') IS NOT NULL AS \"Value\"");

    private static Task<bool> IndexExistsAsync(FlowDbContext db, string index) =>
        ScalarAsync(db, $"SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = '{index}') AS \"Value\"");

    private static Task<bool> ExtensionExistsAsync(FlowDbContext db, string extension) =>
        ScalarAsync(db, $"SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = '{extension}') AS \"Value\"");

    private static async Task<bool> ScalarAsync(FlowDbContext db, string sql) =>
        await db.Database.SqlQueryRaw<bool>(sql).SingleAsync();
}
