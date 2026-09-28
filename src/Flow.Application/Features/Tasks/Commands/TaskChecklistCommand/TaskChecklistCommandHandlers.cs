using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;

/// <summary>
/// Общий путь всех правок чек-листа: actor → задача → право правки → изменение → журнал прогресса → сохранение.
/// Сам чек-лист в индекс поиска не попадает — текст задачи не меняется.
/// </summary>
internal abstract class TaskChecklistHandler(
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
{
    protected async Task<IReadOnlyList<TaskChecklistItemResponse>?> ChangeAsync(Guid actorId, Guid taskId, Action<TaskItem, User> change, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);

        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
            return null;

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        var (oldDone, oldTotal) = (task.ChecklistDone, task.ChecklistTotal);
        change(task, actor);

        // Сначала домен, потом журнал — отказ не оставляет записи. Одна запись на правку; соседние правки
        // одного человека лента и так сворачивает в группу.
        if ((oldDone, oldTotal) != (task.ChecklistDone, task.ChecklistTotal))
            activities.Add(TaskActivity.ChecklistChanged(task.Id, actor.Id, oldDone, oldTotal, task.ChecklistDone, task.ChecklistTotal));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return task.ChecklistResponse();
    }
}

internal sealed class TaskChecklistAddCommandHandler(ITaskItemRepository tasks, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : TaskChecklistHandler(tasks, activities, actors, permissions, projectAccess, unitOfWork), IRequestHandler<TaskChecklistAddCommand, IReadOnlyList<TaskChecklistItemResponse>?>
{
    public Task<IReadOnlyList<TaskChecklistItemResponse>?> Handle(TaskChecklistAddCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.TaskId, (task, _) => task.AddChecklistItem(request.Text), cancellationToken);
}

internal sealed class TaskChecklistUpdateCommandHandler(ITaskItemRepository tasks, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : TaskChecklistHandler(tasks, activities, actors, permissions, projectAccess, unitOfWork), IRequestHandler<TaskChecklistUpdateCommand, IReadOnlyList<TaskChecklistItemResponse>?>
{
    public Task<IReadOnlyList<TaskChecklistItemResponse>?> Handle(TaskChecklistUpdateCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.TaskId, (task, actor) =>
        {
            if (request.Text is not null)
                task.EditChecklistItem(request.ItemId, request.Text);
            if (request.IsDone is { } isDone)
                task.SetChecklistItemDone(request.ItemId, isDone, actor.Id);
        }, cancellationToken);
}

internal sealed class TaskChecklistDeleteCommandHandler(ITaskItemRepository tasks, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : TaskChecklistHandler(tasks, activities, actors, permissions, projectAccess, unitOfWork), IRequestHandler<TaskChecklistDeleteCommand, IReadOnlyList<TaskChecklistItemResponse>?>
{
    public Task<IReadOnlyList<TaskChecklistItemResponse>?> Handle(TaskChecklistDeleteCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.TaskId, (task, _) => task.RemoveChecklistItem(request.ItemId), cancellationToken);
}

internal sealed class TaskChecklistReorderCommandHandler(ITaskItemRepository tasks, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : TaskChecklistHandler(tasks, activities, actors, permissions, projectAccess, unitOfWork), IRequestHandler<TaskChecklistReorderCommand, IReadOnlyList<TaskChecklistItemResponse>?>
{
    public Task<IReadOnlyList<TaskChecklistItemResponse>?> Handle(TaskChecklistReorderCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.TaskId, (task, _) => task.ReorderChecklist(request.ItemIds), cancellationToken);
}
