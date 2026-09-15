using Flow.Infrastructure.Search;
using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Flow.Infrastructure.Persistence.Configurations;

/// <summary>
/// Конфигурация internal (как и сама сущность). HNSW по вектору и GIN по tsvector здесь не объявлены:
/// EF их не умеет, они создаются сырым SQL в миграции AddSearchIndex.
/// </summary>
internal sealed class SearchChunkConfiguration : IEntityTypeConfiguration<SearchChunk>
{
    public void Configure(EntityTypeBuilder<SearchChunk> builder)
    {
        builder.ToTable(SearchSchema.ChunksTable);

        builder.HasKey(c => c.Id);

        builder.Property(c => c.SourceType).HasColumnType("integer");

        builder.Property(c => c.Content)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(c => c.ContentHash)
            .IsRequired()
            .HasColumnType("bytea");

        builder.Property(c => c.Embedding)
            .IsRequired()
            .HasColumnType($"halfvec({SearchSchema.Dimensions})");

        builder.Property(c => c.ModelVersion)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(c => c.SourceUpdatedAt).IsRequired();
        builder.Property(c => c.IndexedAt).IsRequired();

        // Теневое свойство: tsvector считает сама БД (GENERATED ... STORED), .NET его не пишет и не читает.
        // Нужен для лексического плеча гибридного поиска — оно появится на этапе 4.
        builder.Property<NpgsqlTsVector>(SearchSchema.TsvColumn)
            .HasComputedColumnSql(
                $"to_tsvector('{SearchSchema.TextSearchConfiguration}', \"Content\")",
                stored: true);

        // Всё, что нужно воркеру: найти чанки источника, сравнить хеши, переиспользовать чужой вектор.
        builder.HasIndex(c => new { c.SourceType, c.SourceId });
        builder.HasIndex(c => new { c.BoardId, c.IsClosed });
        builder.HasIndex(c => c.ContentHash);

        // Один чанк источника на версию модели: старая и новая версии живут рядом, пока идёт переиндексация.
        builder.HasIndex(c => new { c.SourceType, c.SourceId, c.ChunkIndex, c.ModelVersion }).IsUnique();
    }
}
