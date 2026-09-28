using Flow.Application.Features.Scm;
using Flow.Client.Services;
using Flow.Shared.Contracts.Scm;

namespace Flow.Api.Client;

/// <summary>Git-хостинги (docs/TZ_scm_integration.md) — коды как у ScmController; ошибки ввода и хостинга ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<ScmConnectionResponse>>> GetScmConnections(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new ScmConnectionListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<ScmConnectionResponse>> CreateScmConnection(CreateScmConnectionRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new ScmConnectionCreateCommand(await ActorAsync(), request.Provider, request.Name, request.Token, request.BaseUrl,
            request.AuthKind, request.AppId, request.InstallationId), ct)));

    public Task<ApiResult<ScmConnectionResponse>> UpdateScmConnection(Guid id, UpdateScmConnectionRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmConnectionUpdateCommand(await ActorAsync(), id, request.Name, request.BaseUrl, request.Token,
            request.AppId, request.InstallationId), ct) is { } c
            ? Ok(c) : NotFound<ScmConnectionResponse>());

    public Task<ApiResult<bool>> DeleteScmConnection(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmConnectionDeleteCommand(await ActorAsync(), id), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<ScmConnectionResponse>> CheckScmConnection(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmConnectionCheckCommand(await ActorAsync(), id), ct) is { } c ? Ok(c) : NotFound<ScmConnectionResponse>());

    public Task<ApiResult<IReadOnlyList<ScmRemoteRepositoryResponse>>> GetAvailableRepositories(Guid connectionId, string? query, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmAvailableRepositoriesQuery(await ActorAsync(), connectionId, query), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<ScmRemoteRepositoryResponse>>());

    public Task<ApiResult<ScmRepositoryResponse>> AddScmRepository(AddScmRepositoryRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmRepositoryAddCommand(await ActorAsync(), request.ConnectionId, request.ExternalId), ct) is { } r
            ? Ok(r) : NotFound<ScmRepositoryResponse>());

    public Task<ApiResult<bool>> DisableScmRepository(Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmRepositoryDisableCommand(await ActorAsync(), repositoryId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<IReadOnlyList<ScmDeliveryResponse>>> GetScmDeliveries(Guid repositoryId, ScmDeliveryStatus? status = null, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmDeliveriesQuery(await ActorAsync(), repositoryId, status), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<ScmDeliveryResponse>>());

    public Task<ApiResult<ScmDeliveryResponse>> RetryScmDelivery(Guid deliveryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmDeliveryRetryCommand(await ActorAsync(), deliveryId), ct) is { } d
            ? Ok(d) : NotFound<ScmDeliveryResponse>());

    public Task<ApiResult<bool>> BackfillScmRepository(Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmBackfillCommand(await ActorAsync(), repositoryId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<IReadOnlyList<ScmBoardRepositoryResponse>>> GetBoardRepositories(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmBoardRepositoriesQuery(await ActorAsync(), boardId), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<ScmBoardRepositoryResponse>>());

    public Task<ApiResult<IReadOnlyList<ScmBoardRepositoryResponse>>> SetBoardRepository(Guid boardId, Guid repositoryId, bool bound,
        UpdateScmBindingRequest? settings = null, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new ScmBindCommand(await ActorAsync(), boardId, repositoryId, bound, settings), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<ScmBoardRepositoryResponse>>());

    public Task<ApiResult<TaskDevelopmentResponse>> GetTaskDevelopment(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskDevelopmentQuery(await ActorAsync(), taskId), ct) is { } d ? Ok(d) : NotFound<TaskDevelopmentResponse>());

    public Task<ApiResult<TaskDevelopmentResponse>> CreateScmBranch(Guid taskId, CreateScmBranchRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskScmBranchCreateCommand(await ActorAsync(), taskId, request.RepositoryId, request.Name, request.FromBranch), ct) is { } d
            ? Ok(d) : NotFound<TaskDevelopmentResponse>());

    public Task<ApiResult<TaskDevelopmentResponse>> CreateScmPullRequest(Guid taskId, CreateScmPullRequestRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskScmPullRequestCreateCommand(await ActorAsync(), taskId, request.RepositoryId, request.SourceBranch,
            request.TargetBranch, request.Title, request.Draft), ct) is { } d
            ? Ok(d) : NotFound<TaskDevelopmentResponse>());
}
