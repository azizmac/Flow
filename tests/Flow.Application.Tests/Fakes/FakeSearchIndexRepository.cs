using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Search;

namespace Flow.Application.Tests.Fakes;

/// <summary>Индекс без БД: считает вызовы переиндексации и отдаёт заданную статистику.</summary>
public sealed class FakeSearchIndexRepository : ISearchIndexRepository
{
    private readonly List<(IReadOnlyCollection<SearchSourceType> Types, Guid? BoardId)> _reindexCalls = [];

    public IReadOnlyList<(IReadOnlyCollection<SearchSourceType> Types, Guid? BoardId)> ReindexCalls => _reindexCalls;

    public SearchIndexStatistics Statistics { get; set; } =
        new(QueueTotal: 0, QueueStuck: 0, ChunksByType: new Dictionary<SearchSourceType, int>(), OldestQueuedAt: null);

    public Task<SearchIndexStatistics> GetStatisticsAsync(string modelVersion, int maxAttempts, CancellationToken cancellationToken) =>
        Task.FromResult(Statistics);

    public Task<int> EnqueueAllAsync(IReadOnlyCollection<SearchSourceType> types, Guid? boardId, CancellationToken cancellationToken)
    {
        _reindexCalls.Add((types, boardId));
        return Task.FromResult(types.Count);
    }

    /// <summary>Дозаполнение векторов зовёт только воркер, а он в тестах Application не участвует.</summary>
    public Task<int> EnqueueMissingVectorsAsync(string modelVersion, CancellationToken cancellationToken) =>
        Task.FromResult(0);
}
