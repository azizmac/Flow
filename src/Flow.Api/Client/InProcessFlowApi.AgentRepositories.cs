using System.Net;
using Flow.Application.Features.Agents.Commands.AgentTestAskCommand;
using Flow.Application.Features.Scm;
using Flow.Client.Services;
using Flow.Shared.Contracts.Agents;

namespace Flow.Api.Client;

internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<bool>> SynchronizeGitRepository(Guid boardId, Guid repositoryId, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var result = await mediator.Send(new SynchronizeGitRepositoryCommand(await ActorAsync(), boardId, repositoryId), ct);
            return result is null ? NotFound<bool>() : Ok(result.Value);
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
