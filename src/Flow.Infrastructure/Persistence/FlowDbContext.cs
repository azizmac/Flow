using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence;

public sealed class FlowDbContext(DbContextOptions<FlowDbContext> options) : DbContext(options)
{
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<Status> Statuses => Set<Status>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<TaskType> TaskTypes => Set<TaskType>();

    public DbSet<BoardMember> BoardMembers => Set<BoardMember>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<BoardGroup> BoardGroups => Set<BoardGroup>();
    public DbSet<PermissionSet> PermissionSets => Set<PermissionSet>();

    public DbSet<User> Users => Set<User>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();

    public DbSet<TaskLink> TaskLinks => Set<TaskLink>();

    public DbSet<SavedFilter> SavedFilters => Set<SavedFilter>();
    public DbSet<BoardTemplate> BoardTemplates => Set<BoardTemplate>();
    public DbSet<TaskTemplate> TaskTemplates => Set<TaskTemplate>();

    public DbSet<SavedFilterStar> SavedFilterStars => Set<SavedFilterStar>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Sprint> Sprints => Set<Sprint>();

    public DbSet<Milestone> Milestones => Set<Milestone>();

    public DbSet<CustomFieldDefinition> CustomFields => Set<CustomFieldDefinition>();

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    public DbSet<TaskCodeAlias> TaskCodeAliases => Set<TaskCodeAlias>();

    public DbSet<TaskRecurrence> TaskRecurrences => Set<TaskRecurrence>();

    public DbSet<TaskRecurrenceOccurrence> TaskRecurrenceOccurrences => Set<TaskRecurrenceOccurrence>();

    public DbSet<GitHostConnection> ScmConnections => Set<GitHostConnection>();

    public DbSet<GitRepository> ScmRepositories => Set<GitRepository>();

    public DbSet<GitRepositoryBoard> ScmRepositoryBoards => Set<GitRepositoryBoard>();

    public DbSet<GitDevelopmentLink> ScmLinks => Set<GitDevelopmentLink>();

    public DbSet<GitIntegrationJob> ScmDeliveries => Set<GitIntegrationJob>();

    /// <summary>
    /// Поисковый индекс — проекция, а не домен: наружу из сборки не торчит, Application работает
    /// с ним через ISearchIndexQueue и ISearchIndexRepository.
    /// </summary>
    internal DbSet<SearchChunk> SearchChunks => Set<SearchChunk>();

    internal DbSet<SearchIndexRequest> SearchIndexQueue => Set<SearchIndexRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FlowDbContext).Assembly);
        CustomFieldSql.Register(modelBuilder);
    }
}
