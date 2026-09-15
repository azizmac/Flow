using System.Diagnostics;
using System.Text.RegularExpressions;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchQuery;

internal sealed partial class SearchQueryHandler(
    ISearchQueryRepository index,
    IQueryEmbeddingCache queryEmbeddings,
    SearchOptions options,
    ActorResolver actors)
    : IRequestHandler<SearchQuery, SearchResponse?>
{
    private static readonly SearchSourceType[] AllTypes =
        [SearchSourceType.Task, SearchSourceType.Comment, SearchSourceType.Board, SearchSourceType.User];

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public async Task<SearchResponse?> Handle(SearchQuery request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);

        if (!options.Enabled)
            return null;

        var text = Normalize(request.Text);
        if (text.Length == 0)
            throw new ArgumentException("Запрос поиска не может быть пустым.", nameof(request.Text));

        var query = options.Query;
        var started = Stopwatch.GetTimestamp();

        // Векторная половина нужна во всех режимах, кроме Text. Недоступная модель — не 500:
        // запрос тихо доезжает на полнотексте, а клиент видит degraded.
        float[]? embedding = null;
        var degraded = false;

        if (request.Mode != SearchMode.Text)
        {
            try
            {
                embedding = await queryEmbeddings.GetAsync(text, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Логирует сторона, которая ходила в модель (см. MemoryCachedQueryEmbeddings):
                // Application про логгер не знает и знать не обязан.
                degraded = true;
            }
        }

        // Semantic без вектора искать нечем — в этом случае деградация переводит запрос в Text.
        var useText = request.Mode != SearchMode.Semantic || embedding is null;
        var mode = embedding is null ? SearchMode.Text : request.Mode;

        var criteria = new SearchCriteria(
            Query: text,
            QueryEmbedding: embedding,
            UseText: useText,
            Types: request.Types is { Count: > 0 } types ? types.Distinct().ToArray() : AllTypes,
            BoardId: request.BoardId,
            IncludeArchived: request.IncludeArchived,
            VectorTopN: query.VectorTopN,
            TextTopN: query.TextTopN,
            RrfK: query.RrfK,
            Limit: Math.Clamp(request.Limit, 1, Math.Max(1, query.MaxLimit)),
            Offset: Math.Max(0, request.Offset));

        var page = await index.SearchAsync(criteria, cancellationToken);

        var items = page.Items
            .Select(hit => new SearchResultItem(
                hit.SourceType, hit.SourceId, hit.BoardId, hit.Title, hit.Snippet, hit.Score, hit.TaskCode, hit.UpdatedAt))
            .ToArray();

        return new SearchResponse(
            items,
            page.Total,
            degraded,
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            mode);
    }

    /// <summary>
    /// Схлопывает пробелы и обрезает края: по нормализованной строке кэшируется вектор запроса,
    /// поэтому «  падает   экспорт » и «падает экспорт» не должны считаться дважды.
    /// </summary>
    private static string Normalize(string? text) =>
        text is null ? string.Empty : Whitespace().Replace(text, " ").Trim();
}
