using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Search.Queries.SimilarTasksQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Search;

namespace Flow.Api.Client;

/// <summary>
/// Поиск. Обе операции — чтение, но оба запроса всё равно принимают ActorId: из выдачи выкидываются
/// деактивированные люди, а позже туда же встанет фильтр по правам на проект. Поэтому здесь, в отличие
/// от прочих Query, actor берётся — ровно как контроллеры делают actor.Require().
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<SearchResponse>> Search(
        string query,
        IReadOnlyCollection<SearchSourceType>? types = null,
        Guid? boardId = null,
        bool includeArchived = false,
        int limit = 20,
        int offset = 0,
        bool rerank = false,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            // Внутри Guard: без сессии нужен 401, а не вылетевшее исключение посреди рендера строки поиска.
            var actor = await ActorAsync();

            var response = await mediator.Send(
                new SearchQuery(
                    actor,
                    query,
                    // Пустой список и null для сервера — одно и то же («все типы»); HTTP-клиент пустой
                    // набор в строку запроса не клал, и контроллер тоже сводил его к null.
                    types is { Count: > 0 } ? types as IReadOnlyList<SearchSourceType> ?? [.. types] : null,
                    boardId,
                    includeArchived,
                    // Режим для отладки качества; экраны его никогда не задавали, поэтому всегда гибрид.
                    SearchMode.Hybrid,
                    limit,
                    offset,
                    rerank),
                ct);

            // null — поиск выключен настройкой; у HTTP-клиента это был 404 через Get, то есть ошибка,
            // а не пустая выдача: экран должен сказать «поиск недоступен», а не «ничего не нашлось».
            return response is null ? NotFound<SearchResponse>() : Ok(response);
        });

    public Task<ApiResult<IReadOnlyList<SearchResultItem>>> GetSimilarTasks(Guid taskId, int limit = 5, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();

            var similar = await mediator.Send(new SimilarTasksQuery(actor, taskId, limit), ct);

            // Одинаковый null и на «задачи нет», и на «поиск выключен» — контроллер их тоже не различает.
            return similar is null ? NotFound<IReadOnlyList<SearchResultItem>>() : Ok(similar);
        });
}
