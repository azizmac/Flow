using Flow.Application.Abstractions;

namespace Flow.Infrastructure.Search.Entities;

/// <summary>
/// Запись очереди переиндексации (таблица "SearchIndexQueue"). Очередь живёт в той же базе намеренно:
/// постановка коммитится той же транзакцией, что и правка задачи, — значит, «задача сохранилась,
/// а в индекс не попала» невозможно в принципе, и отдельный брокер не нужен.
/// </summary>
internal sealed class SearchIndexRequest
{
    public Guid Id { get; set; }

    public SearchSourceType SourceType { get; set; }

    public Guid SourceId { get; set; }

    public Guid? BoardId { get; set; }

    public SearchIndexOperation Operation { get; set; }

    /// <summary>0 — живые правки, 1 — массовая переиндексация. Сортировка очереди начинается с него.</summary>
    public int Priority { get; set; }

    public DateTime EnqueuedAt { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>Когда запись снова можно брать: при ошибке отодвигается экспоненциальным backoff'ом.</summary>
    public DateTime NextAttemptAt { get; set; }

    public string? LastError { get; set; }
}
