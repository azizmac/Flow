using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Fql;
using Flow.Client.Services;
using Flow.Shared.Contracts.Filters;

namespace Flow.Api.Client;

/// <summary>Сохранённые фильтры и подсказки FQL — те же коды, что у FiltersController (ошибка FQL несёт позицию, см. Guard).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<FqlSuggestResponse>> SuggestQuery(string query, int position, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new FqlSuggestQuery(await ActorAsync(), query, position), ct)));

    public Task<ApiResult<IReadOnlyList<SavedFilterResponse>>> GetFilters(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new SavedFilterListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<SavedFilterResponse>> GetFilter(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new SavedFilterGetQuery(await ActorAsync(), id), ct) is { } filter ? Ok(filter) : NotFound<SavedFilterResponse>());

    public Task<ApiResult<SavedFilterResponse>> CreateFilter(CreateSavedFilterRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new SavedFilterCreateCommand(await ActorAsync(), request.Name, request.Query, request.Shared), ct)));

    public Task<ApiResult<SavedFilterResponse>> UpdateFilter(Guid id, UpdateSavedFilterRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new SavedFilterUpdateCommand(await ActorAsync(), id, request.Name, request.Query, request.Shared), ct) is { } filter
                ? Ok(filter)
                : NotFound<SavedFilterResponse>());

    public Task<ApiResult<bool>> DeleteFilter(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new SavedFilterDeleteCommand(await ActorAsync(), id), ct) ? Ok(true) : Missing());

    public Task<ApiResult<SavedFilterResponse>> StarFilter(Guid id, bool starred, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new SavedFilterStarCommand(await ActorAsync(), id, starred), ct) is { } filter ? Ok(filter) : NotFound<SavedFilterResponse>());
}
