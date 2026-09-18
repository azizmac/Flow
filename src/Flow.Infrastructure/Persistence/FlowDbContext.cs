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

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<CodeRepository> CodeRepositories => Set<CodeRepository>();

    /// <summary>
    /// Поисковый индекс — проекция, а не домен: наружу из сборки не торчит, Application работает
    /// с ним через ISearchIndexQueue и ISearchIndexRepository.
    /// </summary>
    internal DbSet<SearchChunk> SearchChunks => Set<SearchChunk>();

    internal DbSet<SearchIndexRequest> SearchIndexQueue => Set<SearchIndexRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowDbContext).Assembly);
    }
}
