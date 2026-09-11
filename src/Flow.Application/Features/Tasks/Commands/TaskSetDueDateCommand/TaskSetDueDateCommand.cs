using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;

/// <summary>DueDate = null — снять срок. Права — как на редактирование задачи.</summary>
public sealed record TaskSetDueDateCommand(Guid ActorId, Guid TaskId, DateOnly? DueDate) : IRequest<TaskUpdateResult>;
