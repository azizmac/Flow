using MediatR;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;

/// <summary>
/// Связать задачу с другой (docs/TZ_task_model.md §5): вторая задача — по Id или по коду, в любом проекте, который
/// actor видит. Inward — задача из адреса становится целью («заблокирована»). Права — правка этой задачи; невидимая
/// или несуществующая вторая задача — 400, как будто её нет. Такая связь уже есть — Duplicate (409).
/// Журнал LinkAdded — у обеих задач.
/// </summary>
public sealed record TaskLinkCreateCommand(Guid ActorId, Guid TaskId, TaskLinkType Type, Guid? TargetId = null, string? TargetCode = null, bool Inward = false)
    : IRequest<TaskLinkCreateResult>;
