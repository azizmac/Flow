using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Search;

namespace Flow.Infrastructure.Search.Entities;

/// <summary>
/// Запись очереди индексации (таблица SearchIndexQueue) — outbox изменений. Пишется той же
/// SaveChangesAsync, что и сама правка; забирает её воркер (FOR UPDATE SKIP LOCKED).
/// </summary>
internal sealed class SearchIndexRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public SearchSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    public Guid? BoardId { get; set; }

    public SearchIndexOperation Operation { get; set; }

    /// <summary>0 — живые правки, 1 — массовая переиндексация. Меньше — раньше.</summary>
    public int Priority { get; set; }

    public DateTime EnqueuedAt { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Когда запись можно взять снова: экспоненциальный backoff после ошибки.</summary>
    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
