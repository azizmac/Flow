using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Application.Features.Tasks.Restructure;

// Слияние, разделение, перенос (docs/TZ_task_model.md §6, этап 1E) и поиск задачи по коду с учётом прежних кодов.

/// <summary>Влить Source в Target. Ответ — Target; null — одной из задач нет.</summary>
public sealed record TaskMergeCommand(Guid ActorId, Guid SourceId, Guid TargetId) : IRequest<TaskResponse?>;

/// <summary>Выделить из задачи 1–20 новых. Ответ — новые задачи; null — задачи нет.</summary>
public sealed record TaskSplitCommand(Guid ActorId, Guid SourceId, IReadOnlyList<SplitPart> Parts) : IRequest<IReadOnlyList<TaskResponse>?>;

/// <summary>Что сделает перенос — до подтверждения. null — задачи или проекта нет (или он скрыт).</summary>
public sealed record TaskMovePreviewQuery(Guid ActorId, Guid TaskId, Guid TargetBoardId,
    IReadOnlyDictionary<Guid, Guid>? StatusMap = null, IReadOnlyDictionary<Guid, Guid>? TypeMap = null) : IRequest<TaskMovePreviewResponse?>;

/// <summary>Перенести задачу с поддеревом в другой проект. Ответ — задача с новым кодом.</summary>
public sealed record TaskMoveCommand(Guid ActorId, Guid TaskId, Guid TargetBoardId,
    IReadOnlyDictionary<Guid, Guid>? StatusMap = null, IReadOnlyDictionary<Guid, Guid>? TypeMap = null) : IRequest<TaskResponse?>;

/// <summary>Задача по коду — живому или прежнему (после переноса). Скрытая — null.</summary>
public sealed record TaskGetByCodeQuery(Guid ActorId, string Code) : IRequest<TaskResponse?>;

public static class TaskRestructureLimits
{
    public const int MaxSplitParts = 20;
}

internal sealed class TaskGetByCodeQueryHandler(ITaskItemRepository tasks, TaskResponses responses, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskGetByCodeQuery, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskGetByCodeQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByCodeAsync(request.Code, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        return (await responses.BuildAsync([task], cancellationToken))[0];
    }
}

/// <summary>
/// Слияние: дубль закрывается, а не удаляется — ссылки на его код продолжают работать. Комментарии (с пометкой
/// «_из PROJ-12_»), вложения без повторов по хешу, связи и подзадачи переходят к Target; описание и поля не
/// сливаются — какое правильное, решает человек. Объекты S3 копируются до транзакции, старые удаляются после.
/// Статус дубля — первый финальный проекта, без проверки workflow: это служебное действие, как перевод задач
/// при удалении статуса.
/// </summary>
internal sealed class TaskMergeCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ITaskCommentRepository comments,
    IAttachmentRepository attachments,
    ITaskLinkRepository links,
    ITaskActivityRepository activities,
    IFileStorage storage,
    ISearchIndexQueue searchIndex,
    TaskResponses responses,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskMergeCommand, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskMergeCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var source = await tasks.GetByIdAsync(request.SourceId, cancellationToken);
        var target = await tasks.GetByIdAsync(request.TargetId, cancellationToken);
        if (source is null || target is null)
            return null;
        if (source.Id == target.Id)
            throw new InvalidOperationException("Задачу нельзя слить саму с собой.");

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, source.BoardId, cancellationToken), source);
        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, target.BoardId, cancellationToken), target);

        if (await IsAncestorAsync(source.Id, target, cancellationToken) || await IsAncestorAsync(target.Id, source, cancellationToken))
            throw new InvalidOperationException("Нельзя слить задачу с её предком или подзадачей.");

        var sourceBoard = (await boards.GetByIdAsync(source.BoardId, cancellationToken))!;
        var targetBoard = target.BoardId == sourceBoard.Id ? sourceBoard : (await boards.GetByIdAsync(target.BoardId, cancellationToken))!;

        // Подзадачи — до любых изменений: родитель всегда в том же проекте и выше по уровню.
        var children = await tasks.GetChildrenAsync(source.Id, cancellationToken);
        var targetType = targetBoard.GetTaskType(target.TypeId);
        if (children.Count > 0)
        {
            if (target.BoardId != source.BoardId)
                throw new InvalidOperationException($"У {source.Code.Value} есть подзадачи, а {target.Code.Value} — в другом проекте: сначала перенесите подзадачи.");
            var tooHigh = children.FirstOrDefault(c => sourceBoard.GetTaskType(c.TypeId).Level <= targetType.Level);
            if (tooHigh is not null)
                throw new InvalidOperationException($"Подзадача {tooHigh.Code.Value} не может стать дочерней для {target.Code.Value}: её тип не ниже.");
        }

        // Вложения: повтор по хешу остаётся у дубля, остальные переезжают под ключ Target.
        var hashes = (await attachments.GetByTaskIdAsync(target.Id, cancellationToken)).Select(a => Convert.ToHexString(a.ContentHash)).ToHashSet();
        var moved = new List<(Attachment Attachment, string OldKey)>();
        try
        {
            foreach (var attachment in await attachments.GetByTaskIdForUpdateAsync(source.Id, cancellationToken))
            {
                if (!hashes.Add(Convert.ToHexString(attachment.ContentHash)))
                    continue;
                var oldKey = attachment.Relocate(target.Id, target.BoardId);
                moved.Add((attachment, oldKey));
                await storage.CopyAsync(oldKey, attachment.StorageKey, cancellationToken);
                searchIndex.Enqueue(SearchSourceType.Attachment, attachment.Id, target.BoardId, SearchIndexOperation.Upsert);
            }

            foreach (var comment in await comments.GetByTaskIdForUpdateAsync(source.Id, cancellationToken))
            {
                comment.TransferTo(target.Id, source.Code.Value);
                searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, target.BoardId, SearchIndexOperation.Upsert);
            }

            await MoveLinksAsync(source, target, cancellationToken);

            foreach (var child in children)
            {
                child.SetParent(target, sourceBoard.GetTaskType(child.TypeId), targetType);
                activities.Add(TaskActivity.ParentChanged(child.Id, actor.Id, source.Id, target.Id));
                activities.Add(TaskActivity.ChildAdded(target.Id, actor.Id, child.Id));
            }

            var final = sourceBoard.Statuses.Where(s => s.IsFinal).OrderBy(s => s.SortOrder).First();
            if (!sourceBoard.Statuses.First(s => s.Id == source.StatusId).IsFinal)
            {
                activities.Add(TaskActivity.StatusChanged(source.Id, actor.Id, source.StatusId, final.Id));
                source.ChangeStatus(final.Id);
            }

            if (!await links.ExistsAsync(source.Id, target.Id, TaskLinkType.Duplicates, cancellationToken))
                links.Add(TaskLink.Create(source.Id, target.Id, TaskLinkType.Duplicates, actor.Id));

            activities.Add(TaskActivity.Merged(source.Id, actor.Id, source.Id, target.Id));
            activities.Add(TaskActivity.Merged(target.Id, actor.Id, source.Id, target.Id));
            searchIndex.Enqueue(SearchSourceType.Task, source.Id, source.BoardId, SearchIndexOperation.Upsert);

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Транзакции не было — копии объектов лишние. Best-effort, как при сорвавшейся загрузке.
            foreach (var (attachment, _) in moved)
                await TryDeleteAsync(attachment.StorageKey);
            throw;
        }

        foreach (var (_, oldKey) in moved)
            await TryDeleteAsync(oldKey);

        return (await responses.BuildAsync([target], cancellationToken))[0];
    }

    /// <summary>Связи дубля переходят к Target с тем же направлением; связь между ними самими и повторы — нет.</summary>
    private async Task MoveLinksAsync(TaskItem source, TaskItem target, CancellationToken cancellationToken)
    {
        var added = new HashSet<(Guid, Guid, TaskLinkType)>();
        foreach (var link in await links.GetByTaskIdAsync(source.Id, cancellationToken))
        {
            var other = link.OtherTaskId(source.Id);
            if (other == target.Id)
                continue;

            links.Remove(link);
            var moved = link.IsOutwardFor(source.Id)
                ? TaskLink.Create(target.Id, other, link.Type, link.CreatedById)
                : TaskLink.Create(other, target.Id, link.Type, link.CreatedById);
            if (added.Add((moved.SourceTaskId, moved.TargetTaskId, moved.Type))
                && !await links.ExistsAsync(moved.SourceTaskId, moved.TargetTaskId, moved.Type, cancellationToken))
                links.Add(moved);
        }
    }

    private async Task<bool> IsAncestorAsync(Guid candidateId, TaskItem task, CancellationToken cancellationToken)
    {
        // Глубина иерархии — не больше четырёх уровней.
        for (var parentId = task.ParentId; parentId is { } id; parentId = (await tasks.GetByIdAsync(id, cancellationToken))?.ParentId)
            if (id == candidateId)
                return true;
        return false;
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
            // Мусор в бакете — строк на него уже нет.
        }
    }
}

/// <summary>
/// Разделение: новые задачи в том же проекте, с тем же родителем, типом (архивный — тип по умолчанию), приоритетом,
/// исполнителем (если он ещё активен) и полями; у каждой связь «выделена из». Source не закрывается — остаток
/// работы может остаться в ней; в её журнале — список новых кодов.
/// </summary>
internal sealed class TaskSplitCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    IUserRepository users,
    ITaskLinkRepository links,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    TaskResponses responses,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskSplitCommand, IReadOnlyList<TaskResponse>?>
{
    public async Task<IReadOnlyList<TaskResponse>?> Handle(TaskSplitCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var source = await tasks.GetByIdAsync(request.SourceId, cancellationToken);
        if (source is null)
            return null;

        var access = await projectAccess.GetAsync(actor, source.BoardId, cancellationToken);
        permissions.EnsureCanEditTask(actor, access, source);
        permissions.EnsureCanCreateTask(access);

        if (request.Parts is not { Count: > 0 and <= TaskRestructureLimits.MaxSplitParts })
            throw new ArgumentException($"Разделить можно на 1–{TaskRestructureLimits.MaxSplitParts} задач.", nameof(request.Parts));

        var board = (await boards.GetByIdAsync(source.BoardId, cancellationToken))!;
        var parent = source.ParentId is { } parentId ? await tasks.GetByIdAsync(parentId, cancellationToken) : null;
        var type = board.GetTaskType(source.TypeId);
        var assignee = source.AssigneeId is { } assigneeId ? await users.GetByIdAsync(assigneeId, cancellationToken) : null;

        var created = new List<TaskItem>();
        foreach (var part in request.Parts)
        {
            var task = board.CreateTask(part.Title, string.IsNullOrWhiteSpace(part.Description) ? null : part.Description.Trim(),
                createdById: actor.Id, typeId: type.IsArchived ? null : type.Id, rank: FractionalIndex.First);
            if (parent is not null)
                task.SetParent(parent, board.GetTaskType(task.TypeId), board.GetTaskType(parent.TypeId));
            task.SetPriority(source.Priority);
            if (assignee is { IsActive: true })
                task.Assign(assignee.Id);
            if (task.TypeId == source.TypeId)
                task.CopyCustomFieldsFrom(source);

            tasks.Add(task);
            activities.Add(TaskActivity.Created(task.Id, actor.Id));
            if (parent is not null)
                activities.Add(TaskActivity.ChildAdded(parent.Id, actor.Id, task.Id));
            links.Add(TaskLink.Create(task.Id, source.Id, TaskLinkType.SplitFrom, actor.Id));
            activities.Add(TaskActivity.LinkAdded(task.Id, actor.Id, TaskLinkType.SplitFrom, true, source.Id));
            searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
            created.Add(task);
        }

        activities.Add(TaskActivity.Split(source.Id, actor.Id, created.Select(t => t.Code.Value)));

        // Новые — в конец ручного порядка, в порядке частей; при гонке ключи пересчитываются по свежему максимуму.
        async Task AssignRanks()
        {
            var previous = await tasks.GetMaxRankAsync(board.Id, null, cancellationToken);
            foreach (var task in created)
            {
                task.SetRank(FractionalIndex.Between(previous, null));
                previous = task.Rank;
            }
        }

        await AssignRanks();
        await TaskRanks.SaveAsync(unitOfWork, AssignRanks, cancellationToken);

        return await responses.BuildAsync(created, cancellationToken);
    }
}

internal sealed class TaskMovePreviewQueryHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    IAttachmentRepository attachments,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess) : IRequestHandler<TaskMovePreviewQuery, TaskMovePreviewResponse?>
{
    public async Task<TaskMovePreviewResponse?> Handle(TaskMovePreviewQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var targetAccess = await projectAccess.GetAsync(actor, request.TargetBoardId, cancellationToken);
        var target = targetAccess.CanView ? await boards.GetByIdAsync(request.TargetBoardId, cancellationToken) : null;
        if (target is null)
            return null;

        var source = (await boards.GetByIdAsync(task.BoardId, cancellationToken))!;
        var subtree = await TaskMovePlanner.SubtreeAsync(tasks, task, cancellationToken);
        var plan = TaskMovePlanner.Build(source, target, subtree, request.StatusMap, request.TypeMap);

        var problems = plan.Problems.ToList();
        if (target.Id == source.Id)
            problems.Insert(0, "Задача уже в этом проекте.");
        if (!targetAccess.Permissions.Contains(ProjectPermission.CreateTask))
            problems.Insert(0, $"В проекте {target.Key} у вас нет права создавать задачи.");

        var files = 0;
        foreach (var item in subtree)
            files += (await attachments.GetByTaskIdAsync(item.Id, cancellationToken)).Count;

        return new TaskMovePreviewResponse(target.Id, subtree.Count, files, plan.ParentDropped,
            subtree.Any(t => t.SprintId is not null), subtree.Any(t => t.MilestoneId is not null),
            plan.Statuses, plan.Types, plan.LostFields, problems);
    }
}

/// <summary>
/// Перенос в другой проект: поддерево целиком (корень теряет родителя из исходного проекта), новые коды из
/// счётчика целевого проекта, прежние — в TaskCodeAliases. Статус, тип и поля — по <see cref="TaskMovePlanner"/>,
/// спринт и веха сбрасываются. Вложения копируются под префикс нового проекта до транзакции, старые объекты
/// удаляются после: иначе удаление исходного проекта унесло бы файлы переехавших задач. Индекс — Upsert задач,
/// комментариев и вложений (у чанков меняется проект).
/// </summary>
internal sealed class TaskMoveCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ITaskCommentRepository comments,
    IAttachmentRepository attachments,
    ITaskCodeAliasRepository aliases,
    ITaskActivityRepository activities,
    IFileStorage storage,
    ISearchIndexQueue searchIndex,
    IGitDevelopmentLinkRepository developmentLinks,
    TaskResponses responses,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) : IRequestHandler<TaskMoveCommand, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskMoveCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return null;

        var sourceAccess = await projectAccess.GetAsync(actor, task.BoardId, cancellationToken);
        permissions.EnsureCanEditTask(actor, sourceAccess, task);
        permissions.EnsureCanCreateTask(await projectAccess.GetAsync(actor, request.TargetBoardId, cancellationToken));
        if (request.TargetBoardId == task.BoardId)
            throw new InvalidOperationException("Задача уже в этом проекте.");

        var source = (await boards.GetByIdAsync(task.BoardId, cancellationToken))!;
        var target = await boards.GetByIdAsync(request.TargetBoardId, cancellationToken);
        if (target is null)
            return null;

        var subtree = await TaskMovePlanner.SubtreeAsync(tasks, task, cancellationToken);
        foreach (var descendant in subtree.Skip(1))
            permissions.EnsureCanEditTask(actor, sourceAccess, descendant);

        var plan = TaskMovePlanner.Build(source, target, subtree, request.StatusMap, request.TypeMap);
        if (plan.Problems.Count > 0)
            throw new InvalidOperationException(string.Join(" ", plan.Problems));

        if (task.ParentId is { } oldParent)
            activities.Add(TaskActivity.ChildRemoved(oldParent, actor.Id, task.Id));

        var copied = new List<(Attachment Attachment, string OldKey)>();
        try
        {
            foreach (var item in plan.Items)
            {
                var moved = item.Task;
                foreach (var attachment in await attachments.GetByTaskIdForUpdateAsync(moved.Id, cancellationToken))
                {
                    var oldKey = attachment.Relocate(moved.Id, target.Id);
                    copied.Add((attachment, oldKey));
                    await storage.CopyAsync(oldKey, attachment.StorageKey, cancellationToken);
                    searchIndex.Enqueue(SearchSourceType.Attachment, attachment.Id, target.Id, SearchIndexOperation.Upsert);
                }

                foreach (var comment in await comments.GetByTaskIdAsync(moved.Id, cancellationToken))
                    searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, target.Id, SearchIndexOperation.Upsert);
                // PR и коммиты задачи (этап 5E) остаются при ней, но их чанки несут проект — для фильтра видимости.
                foreach (var link in await developmentLinks.GetByTaskIdAsync(moved.Id, cancellationToken))
                    Flow.Application.Features.Scm.ScmSearch.Upsert(searchIndex, link, target.Id);

                // Ранг — временный: настоящий выставит AssignRanks ниже, по свежему максимуму целевого проекта.
                var oldCode = target.ReceiveTask(moved, item.StatusId, item.TypeId, FractionalIndex.First, item.CustomFieldsJson, item.KeepParent);
                await RememberAsync(oldCode, moved, cancellationToken);
                activities.Add(TaskActivity.Moved(moved.Id, actor.Id, oldCode.Value, moved.Code.Value));
                searchIndex.Enqueue(SearchSourceType.Task, moved.Id, target.Id, SearchIndexOperation.Upsert);
            }

            async Task AssignRanks()
            {
                var previous = await tasks.GetMaxRankAsync(target.Id, null, cancellationToken);
                foreach (var item in plan.Items)
                {
                    item.Task.SetRank(FractionalIndex.Between(previous, null));
                    previous = item.Task.Rank;
                }
            }

            await AssignRanks();
            await TaskRanks.SaveAsync(unitOfWork, AssignRanks, cancellationToken);
        }
        catch
        {
            foreach (var (attachment, _) in copied)
                await TryDeleteAsync(attachment.StorageKey);
            throw;
        }

        foreach (var (_, oldKey) in copied)
            await TryDeleteAsync(oldKey);

        return (await responses.BuildAsync([task], cancellationToken))[0];
    }

    /// <summary>Прежний код — в алиасы; алиас с новым кодом (проект с тем же ключом пересоздали) больше не нужен.</summary>
    private async Task RememberAsync(TaskCode oldCode, TaskItem task, CancellationToken cancellationToken)
    {
        if (await aliases.GetAsync(task.Code.Value, cancellationToken) is { } stale)
            aliases.Remove(stale);

        if (await aliases.GetAsync(oldCode.Value, cancellationToken) is { } existing)
            existing.Repoint(task.Id);
        else
            aliases.Add(TaskCodeAlias.Create(oldCode, task.Id));
    }

    private async Task TryDeleteAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch
        {
            // Мусор в бакете — строк на него уже нет.
        }
    }
}
