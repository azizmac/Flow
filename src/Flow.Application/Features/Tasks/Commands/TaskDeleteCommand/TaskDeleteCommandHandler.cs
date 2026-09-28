using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;

internal sealed class TaskDeleteCommandHandler(ITaskItemRepository tasks, ITaskCommentRepository comments, IAttachmentRepository attachments, IFileStorage storage, ISearchIndexQueue searchIndex, IScmStore scm, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return false;

        var access = await projectAccess.GetAsync(actor, task.BoardId, cancellationToken);
        permissions.EnsureCanEditTask(actor, access, task);

        // Поддерево собирает хендлер, а не каскад БД: каскад прошёл бы мимо очереди индексации и зачистки
        // вложений в хранилище. Глубина не больше четырёх уровней по построению — обход по уровням дешёвый.
        var subtree = new List<TaskItem> { task };
        var level = await tasks.GetChildrenAsync(task.Id, cancellationToken);
        if (level.Count > 0 && !request.Cascade)
            throw new InvalidOperationException($"Task {task.Code.Value} has {level.Count} subtask(s); delete them together or move them first.");

        while (level.Count > 0)
        {
            subtree.AddRange(level);
            var next = new List<TaskItem>();
            foreach (var child in level)
                next.AddRange(await tasks.GetChildrenAsync(child.Id, cancellationToken));
            level = next;
        }

        foreach (var descendant in subtree.Skip(1))
            permissions.EnsureCanEditTask(actor, access, descendant);

        var withFiles = new List<TaskItem>();
        foreach (var item in subtree)
        {
            // Комментарии уйдут каскадом БД, но их чанки привязаны к своим Id — список нужен до удаления.
            foreach (var comment in await comments.GetByTaskIdAsync(item.Id, cancellationToken))
                searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, item.BoardId, SearchIndexOperation.Delete);

            searchIndex.Enqueue(SearchSourceType.Task, item.Id, item.BoardId, SearchIndexOperation.Delete);

            // PR и коммиты задачи (этап 5E) уйдут каскадом так же, как комментарии.
            foreach (var link in await scm.GetLinksByTaskAsync(item.Id, cancellationToken))
                Features.Scm.ScmSearch.Delete(searchIndex, link, item.BoardId);

            // Строки вложений уйдут каскадом, а чанки индекса привязаны к своим Id — список нужен до удаления.
            // Он же отвечает на вопрос, идти ли в хранилище: у задачи без файлов там делать нечего.
            var attached = await attachments.GetByTaskIdAsync(item.Id, cancellationToken);
            foreach (var attachment in attached)
                searchIndex.Enqueue(SearchSourceType.Attachment, attachment.Id, item.BoardId, SearchIndexOperation.Delete);
            if (attached.Count > 0)
                withFiles.Add(item);
        }

        // Детей — раньше родителей: FK ParentId — Restrict. EF упорядочивает удаления по зависимостям сам,
        // обратный порядок регистрации ему просто не мешает.
        foreach (var item in Enumerable.Reverse(subtree))
            tasks.Remove(item);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var item in withFiles)
        {
            try
            {
                // После коммита и best-effort — тот же порядок, что при удалении одного вложения:
                // задача для пользователя удалена, недоступное хранилище не должно ронять запрос.
                await storage.DeleteByPrefixAsync(Attachment.TaskPrefix(item.BoardId, item.Id), cancellationToken);
            }
            catch
            {
                // Объекты останутся мусором в бакете — строк, которые на них ссылаются, уже нет.
            }
        }

        return true;
    }
}
