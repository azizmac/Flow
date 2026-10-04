using Flow.Shared.Contracts.Agents;
using MediatR;

namespace Flow.Application.Features.Agents.Commands.TaskRequirementsReviewCommand;

/// <summary>
/// Обработчик - <see cref="TaskRequirementsReviewCommandHandler"/>
/// </summary>
/// <param name="ActorId"></param>
/// <param name="BoardId"></param>
/// <param name="Title"></param>
/// <param name="Description"></param>
/// <param name="TaskId"></param>
public sealed record TaskRequirementsReviewCommand(
    Guid ActorId,
    Guid BoardId,
    string Title,
    string Description,
    Guid? TaskId = null) : IRequest<TaskRequirementsResponse>;
