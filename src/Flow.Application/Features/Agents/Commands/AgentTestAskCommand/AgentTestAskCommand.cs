using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

public sealed record AgentTestAskCommand(Guid ActorId, string WorkspaceDirectory, string Question) : IRequest<AgentTestResponse>;
