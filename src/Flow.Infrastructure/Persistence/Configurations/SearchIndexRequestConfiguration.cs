using Flow.Infrastructure.Search;
using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flow.Infrastructure.Persistence.Configurations;

internal sealed class SearchIndexRequestConfiguration : IEntityTypeConfiguration<SearchIndexRequest>
{
    public void Configure(EntityTypeBuilder<SearchIndexRequest> builder)
    {
        builder.ToTable(SearchSchema.QueueTable);

        builder.HasKey(r => r.Id);

        builder.Property(r => r.SourceType).HasColumnType("integer");
        builder.Property(r => r.Operation).HasColumnType("integer");

        builder.Property(r => r.EnqueuedAt).IsRequired();
        builder.Property(r => r.NextAttemptAt).IsRequired();

        builder.Property(r => r.LastError).HasColumnType("text");

        // Порядок разбора очереди: сначала живые правки (Priority = 0), внутри приоритета — по возрасту.
        builder.HasIndex(r => new { r.Priority, r.NextAttemptAt });

        // Одна запись на (источник, операция): повторная правка обновляет существующую, а не плодит дубли.
        builder.HasIndex(r => new { r.SourceType, r.SourceId, r.Operation }).IsUnique();
    }
}
