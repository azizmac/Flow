using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Запоминает постановки в очередь вместо записи в БД: тестам фич важно только, что хендлер поставил
/// (или не поставил) нужный источник на переиндексацию.
/// </summary>
public sealed class FakeSearchIndexQueue : ISearchIndexQueue
{
    private readonly List<QueuedRequest> _requests = [];

    public IReadOnlyList<QueuedRequest> Requests => _requests;

    public void Enqueue(
        SearchSourceType sourceType,
        Guid sourceId,
        Guid? boardId,
        SearchIndexOperation operation,
        int priority = 0) =>
        _requests.Add(new QueuedRequest(sourceType, sourceId, boardId, operation, priority));

    public bool Contains(SearchSourceType sourceType, Guid sourceId, SearchIndexOperation operation) =>
        _requests.Any(r => r.SourceType == sourceType && r.SourceId == sourceId && r.Operation == operation);

    public void Clear() => _requests.Clear();

    public sealed record QueuedRequest(
        SearchSourceType SourceType,
        Guid SourceId,
        Guid? BoardId,
        SearchIndexOperation Operation,
        int Priority);
}
