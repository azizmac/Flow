using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>PATCH-семантика: заполненные поля меняются, null — не трогать.</summary>
public sealed record TaskUpdateCommand(Guid ActorId, Guid TaskId, string? Title, string? Description, Guid? StatusId)
    : IRequest<TaskUpdateResult>;
