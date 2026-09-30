using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

internal sealed class AgentTestAskCommandHandler(
    IFlowAgentClient agent,
    IProjectAccess projectAccess,
    IScmStore store,
    IRepositoryWorkspaceService workspaces,
    ActorResolver actors)
    : IRequestHandler<AgentTestAskCommand, AgentTestResponse>
{
    public async Task<AgentTestResponse> Handle(AgentTestAskCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var repository = await store.GetRepositoryAsync(request.RepositoryId, cancellationToken)
            ?? throw new ArgumentException("Репозиторий не найден.", nameof(request.RepositoryId));

        var bindings = await store.GetBindingsAsync(null, repository.Id, cancellationToken);
        var canView = false;
        foreach (var binding in bindings)
        {
            if ((await projectAccess.GetAsync(actor, binding.BoardId, cancellationToken)).CanView)
            {
                canView = true;
                break;
            }
        }

        if (!canView)
            throw new ArgumentException("Репозиторий не найден.", nameof(request.RepositoryId));

        if (repository.LastSyncedCommit is null || repository.SyncState is not
            (GitWorkspaceSyncState.Ready or GitWorkspaceSyncState.Failed))
            throw new InvalidOperationException("У репозитория нет готовой ревизии для анализа.");

        var answer = await agent.AskAsync(workspaces.GetAgentDirectory(repository), request.Question, cancellationToken);
        return new AgentTestResponse(answer.SessionId, answer.Text);
    }
}
