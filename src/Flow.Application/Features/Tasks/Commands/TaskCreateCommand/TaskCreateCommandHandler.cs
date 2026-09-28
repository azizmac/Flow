using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using Flow.Application.Features.CustomFields;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Бросает ArgumentException/InvalidOperationException при невалидных данных (см. Board.CreateTask).</summary>
internal sealed class TaskCreateCommandHandler(IBoardRepository boards, IUserRepository users, ITaskTemplateRepository templates, TaskCustomFields customFields, ITaskItemRepository tasks, ITaskActivityRepository activities, ISearchIndexQueue searchIndex, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCreateCommand, TaskResponse?>
{
    public async Task<TaskResponse?> Handle(TaskCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var access = await projectAccess.GetAsync(actor, request.BoardId, cancellationToken);
        permissions.EnsureCanCreateTask(access);

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        // Новая задача встаёт в конец ручного порядка проекта (docs/TZ_task_model.md §7).
        var rank = FractionalIndex.Between(await tasks.GetMaxRankAsync(board.Id, null, cancellationToken), null);
        var task = board.CreateTask(request.Title, request.Description, request.StatusId, createdById: actor.Id, typeId: request.TypeId, rank: rank);

        // Родитель проверяется доменом: тот же проект, строго выше по уровню. Скрытая или чужая задача — «не найдена».
        TaskItem? parent = null;
        if (request.ParentId is { } parentId)
        {
            parent = await tasks.GetByIdAsync(parentId, cancellationToken);
            if (parent is null || parent.BoardId != board.Id)
                throw new InvalidOperationException($"Parent task {parentId} is not found in project {board.Key}.");

            task.SetParent(parent, board.GetTaskType(task.TypeId), board.GetTaskType(parent.TypeId));
        }

        // Приоритет в журнал отдельно не пишется: запись Created и так фиксирует начальное состояние задачи.
        if (request.Priority is { } priority)
            task.SetPriority(priority);

        // Значения полей — начальное состояние, как приоритет: журнал их не дублирует. Обязательные — сразу.
        if (request.CustomFields is { Count: > 0 } values)
            await customFields.ApplyAsync(board, task, values, actor.Id, journal: false, cancellationToken);
        TaskCustomFields.EnsureRequired(board, task, task.TypeId);

        // Исполнитель сразу при создании — по тем же правилам, что PATCH /assignee: Member назначает только себя,
        // человек активный. Журнал не пишем: запись Created и есть начальное состояние.
        if (request.AssigneeId is { } assigneeId)
        {
            permissions.EnsureCanAssign(actor, access, task, assigneeId);
            var assignee = await users.GetByIdAsync(assigneeId, cancellationToken);
            if (assignee is null || !assignee.IsActive)
                throw new InvalidOperationException("Исполнитель не найден или деактивирован.");
            task.Assign(assignee.Id);
        }

        // Шаблон (этап 3G): название, тип, поля и описание форма уже подставила из него сама — человек мог их поправить;
        // здесь добавляется то, чего в форме нет: чек-лист и подзадачи.
        TaskTemplate? template = null;
        if (request.TemplateId is { } templateId)
        {
            template = await templates.GetByIdAsync(templateId, cancellationToken);
            if (template is null || template.BoardId != board.Id)
                throw new InvalidOperationException("Шаблон задачи в этом проекте не найден.");
            foreach (var text in template.Checklist)
                task.AddChecklistItem(text);
        }

        // Экран создания (docs/TZ_workflow_config.md §3): его «обязательные» проверяет сервер, а не только форма.
        var missing = board.MissingOnCreateScreen(task);
        if (missing.Count > 0)
            throw new InvalidOperationException($"Заполните обязательные поля: {string.Join(", ", missing.Select(m => $"«{m}»"))}.");

        // Board.Tasks не подгружен (не нужен для создания), поэтому EF не отследит новую задачу
        // через изменение коллекции сам — регистрируем её явно.
        tasks.Add(task);
        activities.Add(TaskActivity.Created(task.Id, actor.Id));
        if (parent is not null)
            activities.Add(TaskActivity.ChildAdded(parent.Id, actor.Id, task.Id));
        searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);

        // Подзадачи шаблона — той же транзакцией, сразу за родителем в ручном порядке.
        var subtasks = template is null ? [] : SpawnSubtasks(board, task, template, actor.Id);
        template?.MarkUsed();
        void RankSubtasks()
        {
            var ranks = FractionalIndex.Sequence(task.Rank, subtasks.Count);
            for (var i = 0; i < subtasks.Count; i++)
                subtasks[i].SetRank(ranks[i]);
        }

        RankSubtasks();
        await TaskRanks.SaveAsync(unitOfWork, async () =>
        {
            task.SetRank(FractionalIndex.Between(await tasks.GetMaxRankAsync(board.Id, task.Id, cancellationToken), null));
            RankSubtasks();
        }, cancellationToken);

        return task.ToResponse();
    }

    /// <summary>
    /// Подзадачи из шаблона: тип шаблона, если он ниже родителя, иначе ближайший уровнем ниже (при равных — по
    /// умолчанию). Статус начальный, исполнителя нет; обязательные поля берут значение родителя.
    /// </summary>
    private List<TaskItem> SpawnSubtasks(Board board, TaskItem parent, TaskTemplate template, Guid actorId)
    {
        var parentType = board.GetTaskType(parent.TypeId);
        var created = new List<TaskItem>();
        foreach (var spec in template.Subtasks)
        {
            var type = spec.TypeId is { } typeId && board.TaskTypes.FirstOrDefault(t => t.Id == typeId) is { IsArchived: false } own && own.Level > parentType.Level
                ? own
                : board.TaskTypes.Where(t => !t.IsArchived && t.Level > parentType.Level)
                      .OrderBy(t => t.Level).ThenByDescending(t => t.IsDefault).ThenBy(t => t.SortOrder).FirstOrDefault()
                  ?? throw new InvalidOperationException($"У задачи типа «{parentType.Name}» не бывает подзадач — выберите тип выше.");

            var sub = board.CreateTask(spec.Title, createdById: actorId, typeId: type.Id);
            sub.SetParent(parent, type, parentType);
            foreach (var text in spec.Checklist)
                sub.AddChecklistItem(text);
            foreach (var field in board.MissingRequiredFields(sub, type.Id))
                if (parent.GetCustomField(field.Id) is { } value)
                    sub.SetCustomField(field, value);
            TaskCustomFields.EnsureRequired(board, sub, type.Id);

            tasks.Add(sub);
            activities.Add(TaskActivity.Created(sub.Id, actorId));
            activities.Add(TaskActivity.ChildAdded(parent.Id, actorId, sub.Id));
            searchIndex.Enqueue(SearchSourceType.Task, sub.Id, board.Id, SearchIndexOperation.Upsert);
            created.Add(sub);
        }

        return created;
    }
}
