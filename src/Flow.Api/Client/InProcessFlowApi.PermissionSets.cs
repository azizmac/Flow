using Flow.Application.Features.Boards;
using Flow.Application.Features.PermissionSets;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;

namespace Flow.Api.Client;

/// <summary>Наборы прав (этап 4E) — как PermissionSetsController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<PermissionSetResponse>>> GetPermissionSets(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new PermissionSetListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<PermissionSetResponse>> CreatePermissionSet(SavePermissionSetRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new PermissionSetCreateCommand(await ActorAsync(), request.Name, request.Description,
            request.BaseRole.ToDomainRole(), PermissionSetMapping.ToDomain(request.Permissions)), ct)));

    public Task<ApiResult<PermissionSetResponse>> UpdatePermissionSet(Guid setId, SavePermissionSetRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new PermissionSetUpdateCommand(await ActorAsync(), setId, request.Name, request.Description,
            PermissionSetMapping.ToDomain(request.Permissions)), ct) is { } set ? Ok(set) : NotFound<PermissionSetResponse>());

    public Task<ApiResult<bool>> DeletePermissionSet(Guid setId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new PermissionSetDeleteCommand(await ActorAsync(), setId), ct) ? Ok(true) : NotFound<bool>());
}
