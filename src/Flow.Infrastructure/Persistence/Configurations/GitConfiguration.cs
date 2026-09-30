using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

// Интеграция с Git-хостингами (docs/TZ_git_integration.md §1). Удаление подключения уносит репозитории, их привязки,
// связи и доставки каскадом; удаление задачи — её связи; удаление проекта — привязки.
// SQL-имена таблиц сохранены: переименование кода не меняет существующую схему и историю миграций.

internal sealed class GitHostConnectionConfiguration : IEntityTypeConfiguration<GitHostConnection>
{
    public void Configure(EntityTypeBuilder<GitHostConnection> builder)
    {
        builder.ToTable("ScmConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(GitHostConnection.NameMaxLength).IsRequired();
        builder.Property(c => c.BaseUrl).HasMaxLength(GitHostConnection.UrlMaxLength);
        builder.Property(c => c.SecretProtected).IsRequired();
        builder.Property(c => c.CheckedLogin).HasMaxLength(100);
        builder.Property(c => c.LastError).HasMaxLength(GitHostConnection.ErrorMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class GitRepositoryConfiguration : IEntityTypeConfiguration<GitRepository>
{
    public void Configure(EntityTypeBuilder<GitRepository> builder)
    {
        builder.ToTable("ScmRepositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.FullName).HasMaxLength(GitRepository.NameMaxLength).IsRequired();
        builder.Property(r => r.WebUrl).HasMaxLength(GitHostConnection.UrlMaxLength).IsRequired();
        builder.Property(r => r.DefaultBranch).HasMaxLength(255).IsRequired();
        builder.Property(r => r.WebhookId).HasMaxLength(100);
        builder.Property(r => r.WebhookSecretProtected).IsRequired();
        builder.Property(r => r.SyncState).IsRequired().IsConcurrencyToken().HasComment("Состояние локальной копии репозитория.");
        builder.Property(r => r.LastSyncedCommit).HasMaxLength(GitRepository.CommitMaxLength).HasComment("Commit последней успешной синхронизации.");
        builder.Property(r => r.LastSyncedAt).HasComment("Время последней успешной синхронизации.");
        builder.Property(r => r.LastSyncError).HasMaxLength(GitRepository.SyncErrorMaxLength).HasComment("Краткая причина неудачной синхронизации.");
        builder.HasIndex(r => new { r.ConnectionId, r.ExternalId }).IsUnique();
        builder.HasOne<GitHostConnection>().WithMany().HasForeignKey(r => r.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class GitRepositoryBoardConfiguration : IEntityTypeConfiguration<GitRepositoryBoard>
{
    public void Configure(EntityTypeBuilder<GitRepositoryBoard> builder)
    {
        builder.ToTable("ScmRepositoryBoards");
        builder.HasKey(b => new { b.RepositoryId, b.BoardId });
        builder.HasOne<GitRepository>().WithMany().HasForeignKey(b => b.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Board>().WithMany(b => b.RepositoryBindings).HasForeignKey(b => b.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => b.BoardId);
        // Статус автоперехода удалили — автопереход просто выключается.
        builder.HasOne<Status>().WithMany().HasForeignKey(b => b.OnPullRequestOpenedStatusId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Status>().WithMany().HasForeignKey(b => b.OnPullRequestMergedStatusId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class GitDevelopmentLinkConfiguration : IEntityTypeConfiguration<GitDevelopmentLink>
{
    public void Configure(EntityTypeBuilder<GitDevelopmentLink> builder)
    {
        builder.ToTable("ScmLinks");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.ExternalId).HasMaxLength(GitDevelopmentLink.ExternalIdMaxLength).IsRequired();
        builder.Property(l => l.Url).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.Title).HasMaxLength(GitDevelopmentLink.TitleMaxLength).IsRequired();
        builder.Property(l => l.AuthorLogin).HasMaxLength(100);
        builder.Property(l => l.SourceBranch).HasMaxLength(GitDevelopmentLink.ExternalIdMaxLength);
        builder.Property(l => l.TargetBranch).HasMaxLength(GitDevelopmentLink.ExternalIdMaxLength);
        builder.Property(l => l.Note).HasMaxLength(GitDevelopmentLink.NoteMaxLength);
        builder.HasIndex(l => new { l.TaskId, l.RepositoryId, l.Kind, l.ExternalId }).IsUnique();
        builder.HasIndex(l => new { l.RepositoryId, l.Kind, l.ExternalId });
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<GitRepository>().WithMany().HasForeignKey(l => l.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class GitIntegrationJobConfiguration : IEntityTypeConfiguration<GitIntegrationJob>
{
    public void Configure(EntityTypeBuilder<GitIntegrationJob> builder)
    {
        builder.ToTable("ScmDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DeliveryId).HasMaxLength(GitIntegrationJob.DeliveryIdMaxLength).IsRequired();
        builder.Property(d => d.Event).HasMaxLength(60).IsRequired();
        builder.Property(d => d.LastError).HasMaxLength(GitIntegrationJob.ErrorMaxLength);
        builder.Property(d => d.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(d => new { d.RepositoryId, d.DeliveryId }).IsUnique();
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        builder.HasOne<GitRepository>().WithMany().HasForeignKey(d => d.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
