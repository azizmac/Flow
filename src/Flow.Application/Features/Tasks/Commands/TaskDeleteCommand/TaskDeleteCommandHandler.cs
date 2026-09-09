using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

internal sealed class TaskDeleteCommandHandler(ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        permissions.EnsureCanEditTask(actor, task);

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
