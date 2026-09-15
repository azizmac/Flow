using Flow.Application.Abstractions;
using Pgvector;

namespace Flow.Infrastructure.Search.Entities;

/// <summary>
/// Кусок текста источника вместе с его вектором. Намеренно <c>internal</c> и не вынесен в Flow.Domain:
/// доменных инвариантов здесь нет, а вектор — деталь хранилища. Домен про поиск не знает, индекс —
/// проекция, которую всегда можно пересобрать из задач, комментариев, проектов и людей.
/// Поля с <c>set</c>, а не с фабриками: сущность заполняет только воркер индексации.
/// </summary>
internal sealed class SearchChunk
{
    public Guid Id { get; set; }

    public SearchSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    /// <summary>Для задач и комментариев — доска; для Board — он сам; для User — null. По нему же чистится индекс удалённого проекта.</summary>
    public Guid? BoardId { get; set; }

    /// <summary>0 для коротких источников, которые уместились в один чанк.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>Чистый текст без шапки: шапка уходит только в эмбеддер (см. ChunkHeaderBuilder).</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>SHA-256 от Content: по нему воркер понимает, что текст не менялся, и не зовёт эмбеддер.</summary>
    public byte[] ContentHash { get; set; } = [];

    /// <summary>halfvec(N): половинная точность вдвое дешевле по месту, на косинусной близости незаметна.</summary>
    public HalfVector Embedding { get; set; } = null!;

    /// <summary>
    /// Задача в финальном статусе; для остальных типов всегда false. Меняется обычным UPDATE
    /// при смене статуса — текст от этого не меняется, реэмбеддинг не нужен.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>Версия модели, которой посчитан вектор. Чанки чужой версии в выдачу не годятся.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    public DateTime SourceUpdatedAt { get; set; }

    public DateTime IndexedAt { get; set; }
}
