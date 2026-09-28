using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Связи между задачами (docs/TZ_task_model.md §5). Каскад на обе задачи: связь без одной из них смысла не имеет,
/// и удаление задачи или проекта не должно спотыкаться о чужие связи. Unique (Source, Target, Type) держит
/// «такая связь уже есть» и при гонке; индекс по Target — под входящие связи и счётчик блокировок.
/// </summary>
public sealed class TaskLinkConfiguration : IEntityTypeConfiguration<TaskLink>
{
    public void Configure(EntityTypeBuilder<TaskLink> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Type).IsRequired();
        builder.Property(l => l.CreatedAt).IsRequired();

        builder.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.SourceTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(l => l.TargetTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(l => l.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => new { l.SourceTaskId, l.TargetTaskId, l.Type }).IsUnique();
        builder.HasIndex(l => new { l.TargetTaskId, l.Type });
        builder.HasIndex(l => l.CreatedById);
    }
}
