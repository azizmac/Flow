using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Повторения (docs/TZ_task_model.md §9): одно правило на задачу-образец (unique, каскад от задачи), правило —
/// complex type колонками Rule*. Вхождения — PK (правило, дата): два хоста не создадут дубль; удалённая копия
/// оставляет вхождение (TaskId → null), чтобы дата не сгенерировалась заново.
/// </summary>
internal sealed class TaskRecurrenceConfiguration : IEntityTypeConfiguration<TaskRecurrence>
{
    public void Configure(EntityTypeBuilder<TaskRecurrence> builder)
    {
        builder.ToTable("TaskRecurrences");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasIndex(r => r.TemplateTaskId).IsUnique();
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(r => r.TemplateTaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Board>().WithMany().HasForeignKey(r => r.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(r => r.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.IsActive);
        builder.Property(r => r.LastError).HasMaxLength(TaskRecurrence.ErrorMaxLength);

        builder.ComplexProperty(r => r.Rule, rule =>
        {
            rule.Property(p => p.Frequency).HasColumnName("RuleFrequency").IsRequired();
            rule.Property(p => p.Interval).HasColumnName("RuleInterval").IsRequired();
            rule.Property(p => p.WeekDays).HasColumnName("RuleWeekDays").IsRequired();
            rule.Property(p => p.MonthDay).HasColumnName("RuleMonthDay");
        });
    }
}

internal sealed class TaskRecurrenceOccurrenceConfiguration : IEntityTypeConfiguration<TaskRecurrenceOccurrence>
{
    public void Configure(EntityTypeBuilder<TaskRecurrenceOccurrence> builder)
    {
        builder.ToTable("TaskRecurrenceOccurrences");
        builder.HasKey(o => new { o.RecurrenceId, o.OccursOn });
        builder.HasOne<TaskRecurrence>().WithMany().HasForeignKey(o => o.RecurrenceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TaskItem>().WithMany().HasForeignKey(o => o.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(o => o.TaskId);
    }
}
