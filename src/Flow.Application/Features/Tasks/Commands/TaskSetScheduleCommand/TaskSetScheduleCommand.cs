using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;

/// <summary>
/// Дата начала и срок вместе: по отдельности перенос отрезка упирался бы в проверку «начало ≤ срок» на
/// промежуточном шаге. null — снять. Права — как на редактирование задачи; начало позже срока — 400.
/// Обработчик - <see cref="TaskSetScheduleCommandHandler"/>
/// </summary>
public sealed record TaskSetScheduleCommand(Guid ActorId, Guid TaskId, DateOnly? StartDate, DateOnly? DueDate)
    : IRequest<TaskUpdateResult>;
