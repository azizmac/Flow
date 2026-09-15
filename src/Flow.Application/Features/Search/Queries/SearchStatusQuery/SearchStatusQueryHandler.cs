using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchStatusQuery;

/// <summary>
/// Единственный способ снаружи понять, жив ли поиск. При Search:Enabled=false отвечает нулями и
/// не трогает ни индекс, ни эмбеддер: выключенный поиск не должен стоить ни одного запроса.
/// </summary>
internal sealed class SearchStatusQueryHandler(
    SearchOptions options,
    ISearchIndexStore store,
    IEmbeddingGenerator embeddings,
    ActorResolver actors,
    IPermissionService permissions)
    : IRequestHandler<SearchStatusQuery, SearchStatusResponse>
{
    public async Task<SearchStatusResponse> Handle(SearchStatusQuery request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanViewSearchDiagnostics(await actors.ResolveAsync(request.ActorId, cancellationToken));

        if (!options.Enabled)
            return new SearchStatusResponse(false, false, null, 0, 0, 0, new SearchChunkCountsResponse(0, 0, 0, 0), null);

        var statistics = await store.GetStatisticsAsync(cancellationToken);
        var available = await ProbeEmbedderAsync(cancellationToken);

        return new SearchStatusResponse(
            Enabled: true,
            EmbedderAvailable: available,
            ModelVersion: embeddings.ModelVersion,
            Dimensions: embeddings.Dimensions,
            QueueTotal: statistics.QueueTotal,
            QueueStuck: statistics.QueueStuck,
            ChunksByType: new SearchChunkCountsResponse(
                Count(statistics, SearchSourceType.Task),
                Count(statistics, SearchSourceType.Comment),
                Count(statistics, SearchSourceType.Board),
                Count(statistics, SearchSourceType.User)),
            OldestQueuedAt: statistics.OldestQueuedAt);
    }

    /// <summary>
    /// Отдельной ручки здоровья у llama-server в контракте эмбеддера нет, поэтому пробуем посчитать
    /// короткий вектор. Любая ошибка — «недоступен»: диагностика не должна падать сама.
    /// </summary>
    private async Task<bool> ProbeEmbedderAsync(CancellationToken cancellationToken)
    {
        try
        {
            var vector = await embeddings.EmbedQueryAsync("ping", cancellationToken);
            return vector.Length == embeddings.Dimensions;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static int Count(SearchIndexStatistics statistics, SearchSourceType type) =>
        statistics.ChunksByType.GetValueOrDefault(type);
}
