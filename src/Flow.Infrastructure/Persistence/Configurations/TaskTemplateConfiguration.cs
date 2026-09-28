using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Шаблоны задач (этап 3G): каскад от проекта; тип — SetNull (типы не удаляются, но при удалении проекта каскады
/// идут в одном операторе, и RESTRICT на типе мог бы сработать раньше, чем уйдёт шаблон). Чек-лист — text[],
/// подзадачи и значения полей — jsonb.
/// </summary>
public sealed class TaskTemplateConfiguration : IEntityTypeConfiguration<TaskTemplate>
{
    public void Configure(EntityTypeBuilder<TaskTemplate> builder)
    {
        builder.ToTable("TaskTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).IsRequired().HasMaxLength(TaskTemplate.NameMaxLength);
        builder.Property(t => t.TitlePattern).IsRequired().HasMaxLength(TaskTemplate.TitleMaxLength);
        builder.Property(t => t.Description).HasMaxLength(TaskTemplate.DescriptionMaxLength);
        builder.Property(t => t.CustomFieldsJson).HasColumnName("CustomFields").HasColumnType("jsonb").IsRequired();
        builder.Property(t => t.SubtasksJson).HasColumnName("Subtasks").HasColumnType("jsonb").IsRequired();
        builder.PrimitiveCollection<List<string>>("_checklist").HasColumnName("Checklist").IsRequired();
        builder.Ignore(t => t.Checklist);
        builder.Ignore(t => t.Subtasks);

        builder.HasOne<Board>().WithMany().HasForeignKey(t => t.BoardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<TaskType>().WithMany().HasForeignKey(t => t.TypeId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.BoardId, t.SortOrder });
        builder.HasIndex(t => t.TypeId);
        builder.HasIndex(t => t.CreatedById);
    }
}
