using Flow.Application.Features.Groups;
using Flow.Client.Services;
using Flow.Shared.Contracts.Users;

namespace Flow.Api.Client;

/// <summary>Группы людей (этап 4C) — как GroupsController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<GroupResponse>>> GetGroups(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new GroupListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<GroupResponse>> CreateGroup(SaveGroupRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new GroupCreateCommand(await ActorAsync(), request.Name, request.Description), ct)));

    public Task<ApiResult<GroupResponse>> UpdateGroup(Guid groupId, SaveGroupRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GroupUpdateCommand(await ActorAsync(), groupId, request.Name, request.Description), ct) is { } g
            ? Ok(g)
            : NotFound<GroupResponse>());

    public Task<ApiResult<bool>> DeleteGroup(Guid groupId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GroupDeleteCommand(await ActorAsync(), groupId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<GroupResponse>> SetGroupMember(Guid groupId, Guid userId, bool member, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GroupMemberSetCommand(await ActorAsync(), groupId, userId, member), ct) is { } g
            ? Ok(g)
            : NotFound<GroupResponse>());
}
