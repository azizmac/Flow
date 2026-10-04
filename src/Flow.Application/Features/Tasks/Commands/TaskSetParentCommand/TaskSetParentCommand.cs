using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetParentCommand;

/// <summary>
/// Сменить родителя задачи (docs/TZ_task_model.md §3); ParentId = null — сделать самостоятельной. Права — как правка
/// задачи. Родитель из другого проекта, скрытый или ниже по уровню — 400. Журнал: ParentChanged у задачи,
/// ChildRemoved у старого родителя и ChildAdded у нового.
/// Обработчик - <see cref="TaskSetParentCommandHandler"/>
/// </summary>
public sealed record TaskSetParentCommand(Guid ActorId, Guid TaskId, Guid? ParentId) : IRequest<TaskUpdateResult>;
