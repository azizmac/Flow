using Flow.Infrastructure.Search.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Flow.Infrastructure.Persistence.Configurations;

internal sealed class SearchChunkConfiguration : IEntityTypeConfiguration<SearchChunk>
{
    /// <summary>Имя теневого свойства с полнотекстовым вектором: в сущности его нет, читает его только SQL поиска.</summary>
    public const string TsvProperty = "Tsv";

    public void Configure(EntityTypeBuilder<SearchChunk> builder)
    {
        builder.ToTable("SearchChunks");

        builder.HasKey(c => c.Id);

        // int, как StatusType и UserRole.
        builder.Property(c => c.SourceType).IsRequired();

        builder.Property(c => c.SourceId).IsRequired();

        builder.Property(c => c.BoardId);

        builder.Property(c => c.ChunkIndex).IsRequired();

        builder.Property(c => c.Content).IsRequired();

        builder.Property(c => c.ContentHash).IsRequired();

        // halfvec (fp16) вдвое легче vector: на целевом объёме это гигабайты памяти под HNSW.
        // Столбец необязательный: при выключенных эмбеддингах чанк пишется без вектора и живёт
        // только полнотекстовой половиной (см. SearchEmbeddingsOptions.Enabled).
        builder.Property(c => c.Embedding)
            .HasColumnType($"halfvec({SearchDimensions.Stored})");

        builder.Property(c => c.IsClosed).IsRequired();

        builder.Property(c => c.ModelVersion).IsRequired();

        builder.Property(c => c.SourceUpdatedAt).IsRequired();

        builder.Property(c => c.IndexedAt).IsRequired();

        // Полнотекстовая половина гибрида: считается самой БД при вставке, приложение её не заполняет.
        builder.Property<NpgsqlTsVector>(TsvProperty)
            .HasComputedColumnSql("to_tsvector('russian', \"Content\")", stored: true);

        // HNSW по Embedding и GIN по Tsv создаются сырым SQL в миграции: EF их не умеет.
        builder.HasIndex(c => new { c.SourceType, c.SourceId });

        builder.HasIndex(c => new { c.BoardId, c.IsClosed });

        builder.HasIndex(c => c.ContentHash);

        // Чанки старой версии модели живут рядом с новыми, пока не закончится переиндексация.
        builder.HasIndex(c => new { c.SourceType, c.SourceId, c.ChunkIndex, c.ModelVersion }).IsUnique();
    }
}

/// <summary>
/// Размерность столбца halfvec. Это не то же самое, что Search:Embeddings:Dimensions: тип столбца
/// фиксируется миграцией, а настройка может урезать вектор только до этого размера.
/// </summary>
internal static class SearchDimensions
{
    public const int Stored = 512;
}
