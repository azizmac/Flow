using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

internal sealed class SearchIndexRequestConfiguration : IEntityTypeConfiguration<SearchIndexRequest>
{
    public void Configure(EntityTypeBuilder<SearchIndexRequest> builder)
    {
        builder.ToTable("SearchIndexQueue");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.SourceType).IsRequired();

        builder.Property(r => r.SourceId).IsRequired();

        builder.Property(r => r.BoardId);

        builder.Property(r => r.Operation).IsRequired();

        builder.Property(r => r.Priority).IsRequired();

        builder.Property(r => r.EnqueuedAt).IsRequired();

        builder.Property(r => r.AttemptCount).IsRequired();

        builder.Property(r => r.NextAttemptAt).IsRequired();

        builder.Property(r => r.LastError);

        // Порядок разбора очереди: сначала живые правки, внутри приоритета — по времени постановки.
        builder.HasIndex(r => new { r.Priority, r.NextAttemptAt });

        // Повторная постановка того же источника не плодит строк — см. SearchIndexQueue (ON CONFLICT).
        builder.HasIndex(r => new { r.SourceType, r.SourceId, r.Operation }).IsUnique();
    }
}
