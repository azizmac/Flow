using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

public sealed class StatusConfiguration : IEntityTypeConfiguration<Status>
{
    public void Configure(EntityTypeBuilder<Status> builder)
    {
        builder.HasKey(s => s.Id);
        // Id задаёт домен. Без ValueGeneratedNever EF принимает статус, добавленный в уже сохранённый проект
        // (этап 3A), за существующий и шлёт UPDATE вместо INSERT — та же ловушка, что у TaskType.
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(s => s.SortOrder).IsRequired();

        builder.Property(s => s.IsInitial).IsRequired();

        builder.Property(s => s.IsFinal).IsRequired();

        builder.HasIndex(s => new { s.BoardId, s.SortOrder }).IsUnique();

        builder.HasIndex(s => new { s.BoardId, s.Name }).IsUnique();
    }
}
