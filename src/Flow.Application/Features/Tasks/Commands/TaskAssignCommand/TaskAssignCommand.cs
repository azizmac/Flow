using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskAssignCommand;

/// <summary>
/// UserId = null — снять исполнителя. Назначить можно только существующего активного пользователя.
/// Обработчик - <see cref="TaskAssignCommandHandler"/>
/// </summary>
public sealed record TaskAssignCommand(Guid ActorId, Guid TaskId, Guid? UserId) : IRequest<TaskAssignResult>;
