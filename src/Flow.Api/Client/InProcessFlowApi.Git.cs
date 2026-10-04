using Flow.Application.Features.GitIntegration;
using Flow.Client.Services;
using Flow.Shared.Contracts.GitIntegration;

namespace Flow.Api.Client;

/// <summary>Git-хостинги (docs/TZ_git_integration.md) — коды как у GitController; ошибки ввода и хостинга ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<GitHostConnectionResponse>>> GetGitHostConnections(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new GitHostConnectionListQuery(await ActorAsync()), ct)));

    public Task<ApiResult<GitHostConnectionResponse>> CreateGitHostConnection(CreateGitHostConnectionRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new GitHostConnectionCreateCommand(await ActorAsync(), request.Provider, request.Name, request.Token, request.BaseUrl,
            request.AuthKind, request.AppId, request.InstallationId), ct)));

    public Task<ApiResult<GitHostConnectionResponse>> UpdateGitHostConnection(Guid id, UpdateGitHostConnectionRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitHostConnectionUpdateCommand(await ActorAsync(), id, request.Name, request.BaseUrl, request.Token,
            request.AppId, request.InstallationId), ct) is { } c
            ? Ok(c) : NotFound<GitHostConnectionResponse>());

    public Task<ApiResult<bool>> DeleteGitHostConnection(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitHostConnectionDeleteCommand(await ActorAsync(), id), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<GitHostConnectionResponse>> CheckGitHostConnection(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitHostConnectionCheckCommand(await ActorAsync(), id), ct) is { } c ? Ok(c) : NotFound<GitHostConnectionResponse>());

    public Task<ApiResult<IReadOnlyList<GitRemoteRepositoryResponse>>> GetAvailableRepositories(Guid connectionId, string? query, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitAvailableRepositoriesQuery(await ActorAsync(), connectionId, query), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<GitRemoteRepositoryResponse>>());

    public Task<ApiResult<GitRepositoryResponse>> AddGitRepository(AddGitRepositoryRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitRepositoryAddCommand(await ActorAsync(), request.ConnectionId, request.ExternalId), ct) is { } r
            ? Ok(r) : NotFound<GitRepositoryResponse>());

    public Task<ApiResult<bool>> DisableGitRepository(Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitRepositoryDisableCommand(await ActorAsync(), repositoryId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<IReadOnlyList<GitIntegrationJobResponse>>> GetGitIntegrationJobs(Guid repositoryId, GitIntegrationJobStatus? status = null, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitIntegrationJobsQuery(await ActorAsync(), repositoryId, status), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<GitIntegrationJobResponse>>());

    public Task<ApiResult<GitIntegrationJobResponse>> RetryGitIntegrationJob(Guid deliveryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitIntegrationJobRetryCommand(await ActorAsync(), deliveryId), ct) is { } d
            ? Ok(d) : NotFound<GitIntegrationJobResponse>());

    public Task<ApiResult<bool>> BackfillGitRepository(Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitBackfillCommand(await ActorAsync(), repositoryId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<IReadOnlyList<GitBoardRepositoryResponse>>> GetBoardRepositories(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitBoardRepositoriesQuery(await ActorAsync(), boardId), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<GitBoardRepositoryResponse>>());

    public Task<ApiResult<IReadOnlyList<GitBoardRepositoryResponse>>> SetBoardRepository(Guid boardId, Guid repositoryId, bool bound,
        UpdateGitBindingRequest? settings = null, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new GitBindCommand(await ActorAsync(), boardId, repositoryId, bound, settings), ct) is { } list
            ? Ok(list) : NotFound<IReadOnlyList<GitBoardRepositoryResponse>>());

    public Task<ApiResult<TaskDevelopmentResponse>> GetTaskDevelopment(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskDevelopmentQuery(await ActorAsync(), taskId), ct) is { } d ? Ok(d) : NotFound<TaskDevelopmentResponse>());

    public Task<ApiResult<TaskDevelopmentResponse>> CreateGitBranch(Guid taskId, CreateGitBranchRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskGitBranchCreateCommand(await ActorAsync(), taskId, request.RepositoryId, request.Name, request.FromBranch), ct) is { } d
            ? Ok(d) : NotFound<TaskDevelopmentResponse>());

    public Task<ApiResult<TaskDevelopmentResponse>> CreateGitPullRequest(Guid taskId, CreateGitPullRequestRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskGitPullRequestCreateCommand(await ActorAsync(), taskId, request.RepositoryId, request.SourceBranch,
            request.TargetBranch, request.Title, request.Draft), ct) is { } d
            ? Ok(d) : NotFound<TaskDevelopmentResponse>());
}
