using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md §1). Удаление подключения уносит репозитории, их привязки,
// связи и доставки каскадом; удаление задачи — её связи; удаление проекта — привязки.

internal sealed class ScmConnectionConfiguration : IEntityTypeConfiguration<ScmConnection>
{
    public void Configure(EntityTypeBuilder<ScmConnection> builder)
    {
        builder.ToTable("ScmConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(ScmConnection.NameMaxLength).IsRequired();
        builder.Property(c => c.BaseUrl).HasMaxLength(ScmConnection.UrlMaxLength);
        builder.Property(c => c.SecretProtected).IsRequired();
        builder.Property(c => c.CheckedLogin).HasMaxLength(100);
        builder.Property(c => c.LastError).HasMaxLength(ScmConnection.ErrorMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.CreatedById).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScmRepositoryConfiguration : IEntityTypeConfiguration<ScmRepository>
{
    public void Configure(EntityTypeBuilder<ScmRepository> builder)
    {
        builder.ToTable("ScmRepositories");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.ExternalId).HasMaxLength(100).IsRequired();
        builder.Property(r => r.FullName).HasMaxLength(ScmRepository.NameMaxLength).IsRequired();
        builder.Property(r => r.WebUrl).HasMaxLength(ScmConnection.UrlMaxLength).IsRequired();
        builder.Property(r => r.DefaultBranch).HasMaxLength(255).IsRequired();
        builder.Property(r => r.WebhookId).HasMaxLength(100);
        builder.Property(r => r.WebhookSecretProtected).IsRequired();
        builder.HasIndex(r => new { r.ConnectionId, r.ExternalId }).IsUnique();
        builder.HasOne<ScmConnection>().WithMany().HasForeignKey(r => r.ConnectionId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ScmRepositoryBoardConfiguration : IEntityTypeConfiguration<ScmRepositoryBoard>
{
    public void Configure(EntityTypeBuilder<ScmRepositoryBoard> builder)
    {
        builder.ToTable("ScmRepositoryBoards");
        builder.HasKey(b => new { b.RepositoryId, b.BoardId });
        builder.HasOne<ScmRepository>().WithMany().HasForeignKey(b => b.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Board>().WithMany().HasForeignKey(b => b.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(b => b.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(b => b.BoardId);
        // Статус автоперехода удалили — автопереход просто выключается.
        builder.HasOne<Status>().WithMany().HasForeignKey(b => b.OnPullRequestOpenedStatusId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Status>().WithMany().HasForeignKey(b => b.OnPullRequestMergedStatusId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ScmLinkConfiguration : IEntityTypeConfiguration<ScmLink>
{
    public void Configure(EntityTypeBuilder<ScmLink> builder)
    {
        builder.ToTable("ScmLinks");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.ExternalId).HasMaxLength(ScmLink.ExternalIdMaxLength).IsRequired();
        builder.Property(l => l.Url).HasMaxLength(1000).IsRequired();
        builder.Property(l => l.Title).HasMaxLength(ScmLink.TitleMaxLength).IsRequired();
        builder.Property(l => l.AuthorLogin).HasMaxLength(100);
        builder.Property(l => l.SourceBranch).HasMaxLength(ScmLink.ExternalIdMaxLength);
        builder.Property(l => l.TargetBranch).HasMaxLength(ScmLink.ExternalIdMaxLength);
        builder.Property(l => l.Note).HasMaxLength(ScmLink.NoteMaxLength);
        builder.HasIndex(l => new { l.TaskId, l.RepositoryId, l.Kind, l.ExternalId }).IsUnique();
        builder.HasIndex(l => new { l.RepositoryId, l.Kind, l.ExternalId });
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ScmRepository>().WithMany().HasForeignKey(l => l.RepositoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.AuthorUserId).OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ScmDeliveryConfiguration : IEntityTypeConfiguration<ScmDelivery>
{
    public void Configure(EntityTypeBuilder<ScmDelivery> builder)
    {
        builder.ToTable("ScmDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.DeliveryId).HasMaxLength(ScmDelivery.DeliveryIdMaxLength).IsRequired();
        builder.Property(d => d.Event).HasMaxLength(60).IsRequired();
        builder.Property(d => d.LastError).HasMaxLength(ScmDelivery.ErrorMaxLength);
        builder.Property(d => d.Payload).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(d => new { d.RepositoryId, d.DeliveryId }).IsUnique();
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        builder.HasOne<ScmRepository>().WithMany().HasForeignKey(d => d.RepositoryId).OnDelete(DeleteBehavior.Cascade);
    }
}
