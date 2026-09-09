using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskAssignCommand;

/// <summary>UserId = null — снять исполнителя. Назначить можно только существующего активного пользователя.</summary>
public sealed record TaskAssignCommand(Guid TaskId, Guid? UserId) : IRequest<TaskAssignResult>;
