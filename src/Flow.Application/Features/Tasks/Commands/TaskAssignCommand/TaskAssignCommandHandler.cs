using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskAssignCommand;

/// <summary>Инвариант «назначать можно только активного пользователя» живёт здесь: у TaskItem нет доступа к User.</summary>
internal sealed class TaskAssignCommandHandler(ITaskItemRepository tasks, IUserRepository users, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskAssignCommand, TaskAssignResult>
{
    public async Task<TaskAssignResult> Handle(TaskAssignCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskAssignResult.NotFound();

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

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskAssignResult.Success(task.ToResponse());
    }
}
