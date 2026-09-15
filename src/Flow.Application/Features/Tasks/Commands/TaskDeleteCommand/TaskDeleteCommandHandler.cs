using Flow.Application.Abstractions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

internal sealed class TaskDeleteCommandHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        permissions.EnsureCanEditTask(actor, task);

        // Комментарии уйдут каскадом БД, но в индексе они лежат своими чанками — их идентификаторы
        // после удаления задачи взять уже неоткуда, поэтому собираем их сейчас.
        foreach (var comment in await comments.GetByTaskIdAsync(task.Id, cancellationToken))
            searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, task.BoardId, SearchIndexOperation.Delete);

        searchIndex.Enqueue(SearchSourceType.Task, task.Id, task.BoardId, SearchIndexOperation.Delete);

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
