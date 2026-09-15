namespace Flow.Shared.Contracts.Search;

/// <summary>
/// Диагностика поиска (GET /search/status). При Search:Enabled=false — Enabled=false и нули,
/// к эмбеддеру никто не ходит.
/// </summary>
/// <param name="EmbedderAvailable">Ответил ли эмбеддер на пробный запрос прямо сейчас.</param>
/// <param name="QueueStuck">Записи очереди, исчерпавшие MaxAttempts: их уже никто не заберёт.</param>
public sealed record SearchStatusResponse(
    bool Enabled,
    bool EmbedderAvailable,
    string? ModelVersion,
    int Dimensions,
    int QueueTotal,
    int QueueStuck,
    SearchChunkCountsResponse ChunksByType,
    DateTime? OldestQueuedAt);
