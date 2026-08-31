using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

internal sealed class TaskDeleteCommandHandler(ITaskItemRepository tasks, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskDeleteCommand request, CancellationToken cancellationToken)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
