using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Search;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Очередь индексации без БД: запоминает, что хендлер поставил в индекс. Настоящая реализация пишет
/// строки одной транзакцией с правкой — это проверяется интеграционными тестами, здесь важен только
/// сам факт и параметры постановки.
/// </summary>
public sealed class FakeSearchIndexQueue : ISearchIndexQueue
{
    private readonly List<EnqueuedRequest> _requests = [];

    public IReadOnlyList<EnqueuedRequest> All => _requests;

    public IReadOnlyList<EnqueuedRequest> For(SearchSourceType sourceType, Guid sourceId) =>
        _requests.Where(r => r.SourceType == sourceType && r.SourceId == sourceId).ToList();

    public void Clear() => _requests.Clear();

    public void Enqueue(SearchSourceType sourceType, Guid sourceId, Guid? boardId, SearchIndexOperation operation, int priority = 0) =>
        _requests.Add(new EnqueuedRequest(sourceType, sourceId, boardId, operation, priority));
}

public sealed record EnqueuedRequest(
    SearchSourceType SourceType,
    Guid SourceId,
    Guid? BoardId,
    SearchIndexOperation Operation,
    int Priority);
