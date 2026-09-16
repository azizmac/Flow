using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

internal sealed class TaskDeleteCommandHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, IAttachmentRepository attachments, IFileStorage storage, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        permissions.EnsureCanEditTask(actor, task);

        // Комментарии уйдут каскадом БД, но их чанки привязаны к своим Id — список нужен до удаления.
        foreach (var comment in await comments.GetByTaskIdAsync(task.Id, cancellationToken))
            searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, task.BoardId, SearchIndexOperation.Delete);

        searchIndex.Enqueue(SearchSourceType.Task, task.Id, task.BoardId, SearchIndexOperation.Delete);

        // Строки вложений тоже уйдут каскадом, а объекты в хранилище — нет: их удаляем сами.
        // Проверяем список, чтобы не ходить в S3 при удалении задачи без файлов (таких большинство).
        var hasAttachments = (await attachments.GetByTaskIdAsync(task.Id, cancellationToken)).Count > 0;

        tasks.Remove(task);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (hasAttachments)
        {
            try
            {
                // После коммита и best-effort — тот же порядок, что при удалении одного вложения:
                // задача для пользователя удалена, недоступное хранилище не должно ронять запрос.
                await storage.DeleteByPrefixAsync(Attachment.TaskPrefix(task.BoardId, task.Id), cancellationToken);
            }
            catch
            {
                // Объекты останутся мусором в бакете — строк, которые на них ссылаются, уже нет.
            }
        }

        return true;
    }
}
