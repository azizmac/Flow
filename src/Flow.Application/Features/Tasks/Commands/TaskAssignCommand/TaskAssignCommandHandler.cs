using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskAssignCommand;

/// <summary>Инвариант «назначать можно только активного пользователя» живёт здесь: у TaskItem нет доступа к User.</summary>
internal sealed class TaskAssignCommandHandler(ITaskItemRepository tasks, IUserRepository users, ITaskActivityRepository activities, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskAssignCommand, TaskAssignResult>
{
    public async Task<TaskAssignResult> Handle(TaskAssignCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskAssignResult.NotFound();

        permissions.EnsureCanAssign(actor, task, request.UserId);

        var previous = task.AssigneeId;

        if (request.UserId is null)
        {
            task.Unassign();
        }
        else
        {
            var user = await users.GetByIdAsync(request.UserId.Value, cancellationToken);
            if (user is null)
                return TaskAssignResult.UserNotFound(request.UserId.Value);

            if (!user.IsActive)
                return TaskAssignResult.UserInactive(user.Id);

            task.Assign(user.Id);
        }

        if (task.AssigneeId != previous)
            activities.Add(TaskActivity.AssigneeChanged(task.Id, actor.Id, previous, task.AssigneeId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskAssignResult.Success(task.ToResponse());
    }
}
