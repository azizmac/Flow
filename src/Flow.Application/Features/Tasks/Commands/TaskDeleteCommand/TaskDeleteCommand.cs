using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

/// <summary>
/// true — удалено, false — задача не найдена. Задача с подзадачами без <paramref name="Cascade"/> — 400
/// («у задачи N подзадач»); с флагом удаляется всё поддерево (docs/TZ_task_model.md §3). Права проверяются
/// на каждой задаче поддерева: Member не удалит чужую подзадачу через свою задачу.
/// Обработчик - <see cref="TaskDeleteCommandHandler"/>
/// </summary>
public sealed record TaskDeleteCommand(Guid ActorId, Guid TaskId, bool Cascade = false) : IRequest<bool>;
