using System.Net;
using Flow.Application.Features.Agents.Commands.AgentTestAskCommand;
using Flow.Application.Features.CodeRepositories;
using Flow.Client.Services;
using Flow.Shared.Contracts.Agents;
using Flow.Shared.Contracts.CodeRepositories;

namespace Flow.Api.Client;

internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<CodeRepositoryResponse>>> GetCodeRepositories(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var items = await mediator.Send(new ListCodeRepositoriesQuery(await ActorAsync(), boardId), ct);
            return items is null ? NotFound<IReadOnlyList<CodeRepositoryResponse>>() : Ok(items);
        });

    public Task<ApiResult<CodeRepositoryResponse>> AddCodeRepository(Guid boardId, CreateCodeRepositoryRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var item = await mediator.Send(new CreateCodeRepositoryCommand(await ActorAsync(), boardId, request.Provider, request.Name, request.RemoteUrl, request.Branch), ct);
            return item is null ? NotFound<CodeRepositoryResponse>() : Ok(item);
        });

    public Task<ApiResult<CodeRepositoryResponse>> SynchronizeCodeRepository(Guid boardId, Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var item = await mediator.Send(new SynchronizeCodeRepositoryCommand(await ActorAsync(), boardId, repositoryId), ct);
            return item is null ? NotFound<CodeRepositoryResponse>() : Ok(item);
        });

    public Task<ApiResult<AgentTestResponse>> AskAgent(AgentTestRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            try
            {
                return Ok(await mediator.Send(new AgentTestAskCommand(await ActorAsync(), request.RepositoryId, request.Question), ct));
            }
            catch (HttpRequestException ex)
            {
                return ApiResult<AgentTestResponse>.Fail(ex.Message, HttpStatusCode.BadGateway);
            }
        });
}
