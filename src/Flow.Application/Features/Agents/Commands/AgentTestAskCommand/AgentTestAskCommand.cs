using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.AgentTestAskCommand;

/// <summary>
/// Обработчик - <see cref="AgentTestAskCommandHandler"/>
/// </summary>
public sealed record AgentTestAskCommand(Guid ActorId, Guid RepositoryId, string Question) : IRequest<AgentTestResponse>;
