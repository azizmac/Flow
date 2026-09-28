using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

internal sealed class AgentTestAskCommandHandler(
    IFlowAgentClient agent,
    ICodeRepositoryRepository repositories,
    IRepositoryWorkspaceService workspaces,
    ActorResolver actors)
    : IRequestHandler<AgentTestAskCommand, AgentTestResponse>
{
    public async Task<AgentTestResponse> Handle(AgentTestAskCommand request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);

        var repository = await repositories.GetByIdAsync(request.RepositoryId, cancellationToken)
            ?? throw new ArgumentException("Репозиторий не найден.", nameof(request.RepositoryId));

        if (repository.LastSyncedCommit is null || repository.SyncState is not
            (Flow.Domain.Entities.RepositorySyncState.Ready or Flow.Domain.Entities.RepositorySyncState.Failed))
            throw new InvalidOperationException("У репозитория нет готовой ревизии для анализа.");

        var answer = await agent.AskAsync(workspaces.GetAgentDirectory(repository), request.Question, cancellationToken);
        return new AgentTestResponse(answer.SessionId, answer.Text);
    }
}
