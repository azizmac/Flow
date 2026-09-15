using Flow.Shared.Contracts.Search;

namespace Flow.Application.Abstractions;

/// <summary>
/// Доступ к поисковому индексу для Application: статистика для GET /search/status и массовая
/// постановка в очередь для POST /search/reindex. Сама индексация (чанки, векторы, воркер) —
/// деталь Flow.Infrastructure и наружу не торчит.
/// </summary>
public interface ISearchIndexRepository
{
    /// <summary>Размер очереди, застрявшие записи и число чанков по типам для указанной версии модели.</summary>
    Task<SearchIndexStatistics> GetStatisticsAsync(string modelVersion, int maxAttempts, CancellationToken cancellationToken);

    /// <summary>
    /// Ставит в очередь все подходящие источники пачками (Priority = 1 — позади живых правок).
    /// Возвращает число поставленных записей.
    /// </summary>
    Task<int> EnqueueAllAsync(IReadOnlyCollection<SearchSourceType> types, Guid? boardId, CancellationToken cancellationToken);
}

/// <param name="QueueTotal">Всего записей в очереди.</param>
/// <param name="QueueStuck">Из них исчерпавших MaxAttempts — их никто больше не возьмёт без вмешательства.</param>
/// <param name="OldestQueuedAt">Момент постановки самой старой записи; null — очередь пуста.</param>
public sealed record SearchIndexStatistics(
    int QueueTotal,
    int QueueStuck,
    IReadOnlyDictionary<SearchSourceType, int> ChunksByType,
    DateTime? OldestQueuedAt);
