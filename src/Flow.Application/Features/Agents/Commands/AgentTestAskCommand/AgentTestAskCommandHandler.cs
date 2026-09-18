using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

internal sealed class AgentTestAskCommandHandler(IFlowAgentClient agent, ActorResolver actors)
    : IRequestHandler<AgentTestAskCommand, AgentTestResponse>
{
    private const string WorkspaceRoot = "/workspaces";

    public async Task<AgentTestResponse> Handle(AgentTestAskCommand request, CancellationToken cancellationToken)
    {
        await actors.ResolveAsync(request.ActorId, cancellationToken);

        var workspaceDirectory = request.WorkspaceDirectory.Trim();
        if (workspaceDirectory != WorkspaceRoot
            && !workspaceDirectory.StartsWith(WorkspaceRoot + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Рабочий каталог должен находиться внутри {WorkspaceRoot}.", nameof(request.WorkspaceDirectory));
        }

        var answer = await agent.AskAsync(workspaceDirectory, request.Question, cancellationToken);
        return new AgentTestResponse(answer.SessionId, answer.Text);
    }
}
