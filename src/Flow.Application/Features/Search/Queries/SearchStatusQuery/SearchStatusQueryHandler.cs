using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchStatusQuery;

internal sealed class SearchStatusQueryHandler(
    ISearchIndexRepository index,
    IEmbeddingGenerator embedder,
    SearchOptions options,
    ActorResolver actors,
    IPermissionService permissions)
    : IRequestHandler<SearchStatusQuery, SearchStatusResponse>
{
    public async Task<SearchStatusResponse> Handle(SearchStatusQuery request, CancellationToken cancellationToken)
    {
        // Запрос диагностический, но actor нужен: права на него отдельные (см. IPermissionService).
        permissions.EnsureCanViewSearchDiagnostics(await actors.ResolveAsync(request.ActorId, cancellationToken));

        if (!options.Enabled)
            return new SearchStatusResponse(
                Enabled: false,
                EmbedderAvailable: false,
                ModelVersion: embedder.ModelVersion,
                Dimensions: embedder.Dimensions,
                QueueTotal: 0,
                QueueStuck: 0,
                ChunksByType: new SearchChunkCounts(0, 0, 0, 0, 0),
                OldestQueuedAt: null);

        var statistics = await index.GetStatisticsAsync(embedder.ModelVersion, options.Indexing.MaxAttempts, cancellationToken);
        // Выключенную модель не опрашиваем: пробный вызов уехал бы в таймаут и ничего не сообщил.
        var available = options.Embeddings.Enabled && await embedder.IsAvailableAsync(cancellationToken);

        return new SearchStatusResponse(
            Enabled: true,
            EmbedderAvailable: available,
            ModelVersion: embedder.ModelVersion,
            Dimensions: embedder.Dimensions,
            QueueTotal: statistics.QueueTotal,
            QueueStuck: statistics.QueueStuck,
            ChunksByType: new SearchChunkCounts(
                Count(statistics, SearchSourceType.Task),
                Count(statistics, SearchSourceType.Comment),
                Count(statistics, SearchSourceType.Board),
                Count(statistics, SearchSourceType.User),
                Count(statistics, SearchSourceType.Attachment)),
            OldestQueuedAt: statistics.OldestQueuedAt);
    }

    private static int Count(SearchIndexStatistics statistics, SearchSourceType type) =>
        statistics.ChunksByType.TryGetValue(type, out var count) ? count : 0;
}
