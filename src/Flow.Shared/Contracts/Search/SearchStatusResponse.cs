namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Состояние поискового индекса — GET /search/status (Admin+). При выключенном поиске приходит
/// <c>Enabled = false</c> и нули: к эмбеддеру и в БД за статистикой не ходят.
/// </summary>
/// <param name="Enabled">Значение Search:Enabled.</param>
/// <param name="EmbedderAvailable">Эмбеддер ответил на пробный запрос.</param>
/// <param name="ModelVersion">{модель}:{размерность}:{хеш инструкции} — векторы разных версий несовместимы.</param>
/// <param name="QueueTotal">Записей в очереди индексации.</param>
/// <param name="QueueStuck">Из них исчерпавших MaxAttempts.</param>
/// <param name="OldestQueuedAt">Момент постановки самой старой записи; null — очередь пуста.</param>
public sealed record SearchStatusResponse(
    bool Enabled,
    bool EmbedderAvailable,
    string ModelVersion,
    int Dimensions,
    int QueueTotal,
    int QueueStuck,
    SearchChunkCounts ChunksByType,
    DateTime? OldestQueuedAt);

/// <summary>Число чанков текущей ModelVersion по типам источников.</summary>
public sealed record SearchChunkCounts(int Task, int Comment, int Board, int User);
