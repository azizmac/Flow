namespace Flow.Application.Abstractions;

/// <summary>
/// Чтение состояния индекса и массовая постановка в очередь. Всё, что Application нужно знать про
/// хранилище векторов: сами чанки — деталь Flow.Infrastructure и в Application не выносятся.
/// </summary>
public interface ISearchIndexStore
{
    /// <summary>Сводка для GET /search/status. К эмбеддеру не ходит.</summary>
    Task<SearchIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Ставит в очередь всё подходящее с <c>Priority = 1</c> пачками (живые правки с Priority = 0 идут раньше).
    /// Пустой <paramref name="types"/> — все типы; <paramref name="boardId"/> сужает до одного проекта
    /// (для типа User игнорируется — люди не принадлежат проекту). Возвращает число поставленных записей.
    /// </summary>
    Task<int> EnqueueAllAsync(
        IReadOnlyCollection<SearchSourceType> types,
        Guid? boardId,
        CancellationToken cancellationToken);
}

/// <summary>Счётчики индекса. <paramref name="QueueStuck"/> — записи, исчерпавшие MaxAttempts.</summary>
public sealed record SearchIndexStatistics(
    int QueueTotal,
    int QueueStuck,
    IReadOnlyDictionary<SearchSourceType, int> ChunksByType,
    DateTime? OldestQueuedAt)
{
    public static SearchIndexStatistics Empty { get; } =
        new(0, 0, new Dictionary<SearchSourceType, int>(), null);
}
