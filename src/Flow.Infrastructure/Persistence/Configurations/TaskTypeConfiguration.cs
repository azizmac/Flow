using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class TaskTypeConfiguration : IEntityTypeConfiguration<TaskType>
{
    public void Configure(EntityTypeBuilder<TaskType> builder)
    {
        builder.HasKey(t => t.Id);

        // Id задаёт домен. Без ValueGeneratedNever EF принимает новый тип, найденный через Board.TaskTypes
        // у уже сохранённой доски, за существующий (ключ-то задан) и шлёт UPDATE, который не затрагивает ни строки.
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(TaskType.NameMaxLength);

        builder.Property(t => t.Kind).IsRequired();

        builder.Property(t => t.SortOrder).IsRequired();

        builder.Property(t => t.IsDefault).IsRequired();

        builder.Property(t => t.IsArchived).IsRequired();

        // Уровень выводится из вида (TaskTypeKind.Level) — в БД его нет.
        builder.Ignore(t => t.Level);

        // Имя уникально без учёта регистра в домене; в БД — страховка точного совпадения, как у статусов.
        builder.HasIndex(t => new { t.BoardId, t.Name }).IsUnique();
    }
}
