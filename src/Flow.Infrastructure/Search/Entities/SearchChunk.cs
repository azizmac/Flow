using Flow.Shared.Contracts.Search;
using Pgvector;

namespace Flow.Infrastructure.Search.Entities;

/// <summary>
/// Кусок текста источника вместе с вектором — то, по чему ищут. Поисковый индекс это проекция,
/// а не домен: инвариантов у записи нет, а <see cref="HalfVector"/> — деталь хранилища, поэтому
/// класс живёт здесь и остаётся internal, а Flow.Domain не знает про pgvector.
/// </summary>
internal sealed class SearchChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SearchSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    /// <summary>Задача и комментарий — их доска; проект — он сам; человек — null.</summary>
    public Guid? BoardId { get; set; }

    /// <summary>Порядковый номер чанка внутри источника; 0 у коротких источников.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Чистый текст без шапки — для подсветки и полнотекстовой половины гибрида.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>SHA-256 от Content: не переэмбеддить неизменившийся текст и переиспользовать готовый вектор.</summary>
    public byte[] ContentHash { get; set; } = [];

    /// <summary>
    /// null — чанк проиндексирован без модели (Search:Embeddings:Enabled=false): текст ищется,
    /// вектор дозаполнится при следующей индексации источника. Векторная половина такие чанки
    /// пропускает явным условием, а не полагается на порядок NULL в ORDER BY.
    /// </summary>
    public HalfVector? Embedding { get; set; }

    /// <summary>Задача в финальном статусе; у остальных типов false. Меняется UPDATE'ом, без реэмбеддинга.</summary>
    public bool IsClosed { get; set; }

    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>Момент версии источника, с которой снят чанк.</summary>
    public DateTime SourceUpdatedAt { get; set; }

    public DateTime IndexedAt { get; set; }
}
