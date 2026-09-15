using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SimilarTasksQuery;

internal sealed class SimilarTasksQueryHandler(
    ISearchQueryRepository index,
    ITaskItemRepository tasks,
    SearchOptions options,
    ActorResolver actors)
    : IRequestHandler<SimilarTasksQuery, IReadOnlyList<SearchResultItem>?>
{
    /// <summary>Больше десятка похожих никто не читает, а стоимость свёртки растёт.</summary>
    private const int MaxLimit = 10;

    public async Task<IReadOnlyList<SearchResultItem>?> Handle(SimilarTasksQuery request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);

        if (!options.Enabled)
            return null;

        // Задачи нет — это 404, а не пустой список: клиент не должен показывать блок в пустоту.
        if (await tasks.GetByIdAsync(request.TaskId, cancellationToken) is null)
            return null;

        var hits = await index.FindSimilarAsync(request.TaskId, Math.Clamp(request.Limit, 1, MaxLimit), cancellationToken);

        return hits
            .Select(hit => new SearchResultItem(
                hit.SourceType, hit.SourceId, hit.BoardId, hit.Title, hit.Snippet, hit.Score, hit.TaskCode, hit.UpdatedAt, hit.ParentId))
            .ToArray();
    }
}
