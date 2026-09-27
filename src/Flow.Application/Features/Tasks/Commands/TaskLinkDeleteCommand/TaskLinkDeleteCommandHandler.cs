using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskLinkDeleteCommand;

internal sealed class TaskLinkDeleteCommandHandler(
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ITaskActivityRepository activities,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskLinkDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskLinkDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var link = await links.GetByIdAsync(request.LinkId, cancellationToken);
        if (link is null)
            return false;

        // Хватает права править любую из двух задач. Скрытые стороны не рассматриваются вовсе;
        // если не видна ни одна — связи для actor'а нет.
        ForbiddenException? denied = null;
        var anyVisible = false;
        foreach (var taskId in new[] { link.SourceTaskId, link.TargetTaskId })
        {
            var task = await tasks.GetByIdAsync(taskId, cancellationToken);
            if (task is null)
                continue;

            var access = await projectAccess.GetAsync(actor, task.BoardId, cancellationToken);
            if (!access.CanView)
                continue;

            anyVisible = true;
            try
            {
                permissions.EnsureCanEditTask(actor, access, task);
                denied = null;
                break;
            }
            catch (ForbiddenException ex)
            {
                denied = ex;
            }
        }

        if (!anyVisible)
            return false;
        if (denied is not null)
            throw denied;

        links.Remove(link);
        activities.Add(TaskActivity.LinkRemoved(link.SourceTaskId, actor.Id, link.Type, true, link.TargetTaskId));
        activities.Add(TaskActivity.LinkRemoved(link.TargetTaskId, actor.Id, link.Type, false, link.SourceTaskId));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }
}
