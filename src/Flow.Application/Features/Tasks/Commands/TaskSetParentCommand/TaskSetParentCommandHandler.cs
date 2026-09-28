using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskSetParentCommand;

internal sealed class TaskSetParentCommandHandler(
    ITaskItemRepository tasks,
    IBoardRepository boards,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskSetParentCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskSetParentCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (request.ParentId == task.ParentId)
            return TaskUpdateResult.Success(task.ToResponse());

        // Родитель ищется только в проекте задачи: иерархии через проекты нет, а задача другого проекта
        // (возможно, скрытого) должна выглядеть так же, как несуществующая.
        TaskItem? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await tasks.GetByIdAsync(parentId, cancellationToken);
            if (parent is null || parent.BoardId != task.BoardId)
                throw new InvalidOperationException($"Parent task {parentId} is not found in the task's project.");
        }

        var board = await boards.GetByIdAsync(task.BoardId, cancellationToken)
            ?? throw new InvalidOperationException($"Board {task.BoardId} is not found.");

        var oldParentId = task.ParentId;
        task.SetParent(parent, board.GetTaskType(task.TypeId), parent is null ? null : board.GetTaskType(parent.TypeId));

        // Сначала домен, потом журнал: отказ по уровням не оставляет записей. Индекс не трогаем — текст не менялся.
        activities.Add(TaskActivity.ParentChanged(task.Id, actor.Id, oldParentId, task.ParentId));
        if (oldParentId is { } oldId)
            activities.Add(TaskActivity.ChildRemoved(oldId, actor.Id, task.Id));
        if (task.ParentId is { } newId)
            activities.Add(TaskActivity.ChildAdded(newId, actor.Id, task.Id));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var children = await tasks.CountChildrenAsync([task.Id], cancellationToken);
        return TaskUpdateResult.Success(task.ToResponse(children: children.GetValueOrDefault(task.Id)));
    }
}
