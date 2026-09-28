using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

public sealed record AgentTestAskCommand(Guid ActorId, Guid RepositoryId, string Question) : IRequest<AgentTestResponse>;
