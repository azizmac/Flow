using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>Прежние коды задач (docs/TZ_task_model.md §6): PK — код, удаление задачи уносит её алиасы.</summary>
internal sealed class TaskCodeAliasConfiguration : IEntityTypeConfiguration<TaskCodeAlias>
{
    public void Configure(EntityTypeBuilder<TaskCodeAlias> builder)
    {
        builder.ToTable("TaskCodeAliases");
        builder.HasKey(a => a.Code);
        builder.Property(a => a.Code).HasMaxLength(32).ValueGeneratedNever();
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => a.TaskId);
    }
}
