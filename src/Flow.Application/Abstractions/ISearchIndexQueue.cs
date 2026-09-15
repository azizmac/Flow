namespace Flow.Application.Abstractions;

/// <summary>
/// Очередь переиндексации. Хендлеры ставят сюда источники рядом с изменением сущности, не зная ни про
/// векторы, ни про эмбеддер. При <c>Search:Enabled=false</c> реализация — no-op: очередь не растёт.
/// </summary>
public interface ISearchIndexQueue
{
    /// <summary>Ставит источник в очередь. Коммитится той же SaveChangesAsync, что и изменение сущности.</summary>
    void Enqueue(SearchSourceType sourceType, Guid sourceId, Guid? boardId, SearchIndexOperation operation, int priority = 0);
}
