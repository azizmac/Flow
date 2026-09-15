using Flow.Application.Abstractions;

namespace Flow.Application.Tests.Fakes;

/// <summary>Индекс без индекса: тестам фич важны права и ветвление, а не содержимое чанков.</summary>
public sealed class FakeSearchIndexStore : ISearchIndexStore
{
    public SearchIndexStatistics Statistics { get; set; } = SearchIndexStatistics.Empty;

    /// <summary>Что и с каким сужением просили переиндексировать.</summary>
    public List<(IReadOnlyCollection<SearchSourceType> Types, Guid? BoardId)> Reindexed { get; } = [];

    public int QueuedPerType { get; set; } = 1;

    public Task<SearchIndexStatistics> GetStatisticsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Statistics);

    public Task<int> EnqueueAllAsync(
        IReadOnlyCollection<SearchSourceType> types,
        Guid? boardId,
        CancellationToken cancellationToken)
    {
        Reindexed.Add((types, boardId));
        return Task.FromResult(types.Count * QueuedPerType);
    }
}
