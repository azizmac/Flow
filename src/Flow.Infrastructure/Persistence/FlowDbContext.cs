using Flow.Domain.Entities;
using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence;

public sealed class FlowDbContext(DbContextOptions<FlowDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<Status> Statuses => Set<Status>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<User> Users => Set<User>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();

    // Индекс поиска — internal: наружу он не торчит, работать с ним можно только изнутри Flow.Infrastructure
    // (очередь, воркер, диагностика). Для Application это ISearchIndexQueue/ISearchIndexStore.
    internal DbSet<SearchChunk> SearchChunks => Set<SearchChunk>();

    internal DbSet<SearchIndexRequest> SearchIndexQueue => Set<SearchIndexRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector: расширение объявлено в модели, чтобы EF знал про тип halfvec; создаётся оно
        // миграцией AddSearchIndex (CREATE EXTENSION IF NOT EXISTS vector).
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowDbContext).Assembly);
    }
}
