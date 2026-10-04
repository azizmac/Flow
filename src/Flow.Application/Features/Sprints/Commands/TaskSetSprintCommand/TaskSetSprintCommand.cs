using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;

/// <summary>
/// Поле «Спринт» в карточке: перенести задачу в спринт своего проекта или в бэклог (null). Это правка задачи
/// (EnsureCanEditTask); завершённый или чужой спринт — 400.
/// Обработчик - <see cref="TaskSetSprintCommandHandler"/>
/// </summary>
public sealed record TaskSetSprintCommand(Guid ActorId, Guid TaskId, Guid? SprintId) : IRequest<TaskUpdateResult>;
