using System.ComponentModel;
using System.Globalization;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskChecklistQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Flow.Api.Mcp.Tools;

/// <summary>
/// Задачи: отбор, карточка, создание и правка. Удаления, слияния и переноса между проектами здесь нет намеренно —
/// это необратимые операции, и агенту они достаются только через интерфейс или API.
/// </summary>
[McpServerToolType]
public sealed class McpTaskTools(McpContext flow)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    [McpServerTool(Name = "search_tasks", Title = "Найти задачи", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Список задач с фильтрами: проект, исполнитель, подстрока и/или FQL (синтаксис — в инструкциях сервера). "
                 + "Фильтры складываются через AND. Возвращает страницу и общее число подходящих (matched).")]
    public Task<McpTaskPage> SearchTasks(
        [Description("FQL, например: assignee = me() AND statusCategory != done ORDER BY priority DESC.")] string? fql = null,
        [Description("Ключ проекта или Id. Без него — по всем видимым проектам.")] string? project = null,
        [Description("Исполнитель: username, «me» или «none» (без исполнителя).")] string? assignee = null,
        [Description("Подстрока в названии или коде задачи.")] string? text = null,
        [Description("Размер страницы, до 100.")] int limit = DefaultLimit,
        [Description("Сдвиг для следующей страницы.")] int offset = 0,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var boardId = project is null ? (Guid?)null : (await flow.ResolveBoardAsync(project, cancellationToken)).Id;
            var assigneeId = assignee is null ? null : await flow.ResolveUserAsync(assignee, cancellationToken);
            var unassigned = assignee is not null && assigneeId is null;
            var size = Math.Clamp(limit, 1, MaxLimit);
            var skip = Math.Max(offset, 0);

            var page = await flow.Mediator.Send(new TaskSearchQuery(
                    flow.ActorId,
                    BoardId: boardId,
                    AssigneeId: assigneeId,
                    Unassigned: unassigned,
                    Query: string.IsNullOrWhiteSpace(text) ? null : text,
                    Limit: size,
                    Offset: skip,
                    Fql: string.IsNullOrWhiteSpace(fql) ? null : fql),
                cancellationToken);

            var items = new List<McpTask>(page.Items.Count);
            foreach (var task in page.Items)
                items.Add(await flow.ToViewAsync(task, withDescription: false, cancellationToken));

            var next = skip + items.Count < page.Matched ? $"offset={skip + items.Count}" : null;
            return new McpTaskPage(page.Matched, skip, items, next);
        });

    [McpServerTool(Name = "get_task", Title = "Задача", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description("Карточка задачи целиком: описание, статус, исполнитель, даты, оценка, чек-лист и комментарии.")]
    public Task<McpTaskDetails> GetTask(
        [Description("Код задачи (PROJ-142) или Id.")] string task,
        [Description("Добавить комментарии.")] bool includeComments = true,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var view = await flow.ToViewAsync(found, withDescription: true, cancellationToken);

            var checklist = found.ChecklistTotal == 0
                ? null
                : (await flow.Mediator.Send(new TaskChecklistQuery(flow.ActorId, found.Id), cancellationToken))?
                    .Select(i => new McpChecklistItem(i.Id, i.Text, i.IsDone)).ToList();

            List<McpComment>? comments = null;
            if (includeComments && found.CommentCount > 0)
            {
                comments = [];
                foreach (var c in await flow.Mediator.Send(new TaskCommentListQuery(flow.ActorId, found.Id), cancellationToken) ?? [])
                    comments.Add(new McpComment(c.Id, await flow.UsernameAsync(c.AuthorId, cancellationToken), c.Body, c.CreatedAt, c.EditedAt));
            }

            return new McpTaskDetails(view, checklist, comments);
        });

    [McpServerTool(Name = "create_task", Title = "Создать задачу", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Создаёт задачу в проекте. Статус по умолчанию — начальный, тип — тип проекта по умолчанию. "
                 + "Имена статусов и типов — из get_project.")]
    public Task<McpTask> CreateTask(
        [Description("Ключ проекта или Id.")] string project,
        [Description("Название.")] string title,
        [Description("Описание в Markdown.")] string? description = null,
        [Description("Тип: имя типа проекта или вид (epic, story, task, bug, subtask).")] string? type = null,
        [Description("Приоритет: None, Low, Medium, High, Critical.")] string? priority = null,
        [Description("Статус: имя статуса проекта.")] string? status = null,
        [Description("Исполнитель: username или «me».")] string? assignee = null,
        [Description("Родительская задача: код или Id (уровнем выше по типу: эпик → история → задача → подзадача).")] string? parent = null,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var board = await flow.ResolveBoardAsync(project, cancellationToken);
            var parentId = parent is null ? (Guid?)null : (await flow.ResolveTaskAsync(parent, cancellationToken)).Id;
            var assigneeId = assignee is null ? null : await flow.ResolveUserAsync(assignee, cancellationToken);

            var created = await flow.Mediator.Send(new TaskCreateCommand(
                    flow.ActorId,
                    board.Id,
                    title,
                    description,
                    status is null ? null : McpContext.ResolveStatus(board, status).Id,
                    type is null ? null : McpContext.ResolveType(board, type).Id,
                    priority is null ? null : McpContext.ParsePriority(priority).ToDomainPriority(),
                    parentId,
                    AssigneeId: assigneeId),
                cancellationToken) ?? throw new McpException($"Проект «{project}» не найден.");

            return await flow.ToViewAsync(created, withDescription: true, cancellationToken);
        });

    [McpServerTool(Name = "update_task", Title = "Изменить задачу", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Меняет название, описание, статус, тип или приоритет. Не переданные поля не трогаются. "
                 + "Смена статуса проходит workflow проекта — при отказе вернутся причины.")]
    public Task<McpTask> UpdateTask(
        [Description("Код задачи или Id.")] string task,
        [Description("Новое название.")] string? title = null,
        [Description("Новое описание в Markdown (заменяет прежнее целиком).")] string? description = null,
        [Description("Новый статус: имя статуса проекта.")] string? status = null,
        [Description("Новый тип: имя или вид.")] string? type = null,
        [Description("Новый приоритет: None, Low, Medium, High, Critical.")] string? priority = null,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var board = await flow.BoardAsync(found.BoardId, cancellationToken)
                ?? throw new McpException($"Задача «{task}» не найдена.");

            var result = await flow.Mediator.Send(new TaskUpdateCommand(
                    flow.ActorId,
                    found.Id,
                    title,
                    description,
                    status is null ? null : McpContext.ResolveStatus(board, status).Id,
                    type is null ? null : McpContext.ResolveType(board, type).Id,
                    priority is null ? null : McpContext.ParsePriority(priority).ToDomainPriority()),
                cancellationToken);

            return await UpdatedAsync(task, result, cancellationToken);
        });

    [McpServerTool(Name = "assign_task", Title = "Назначить исполнителя", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Назначает исполнителя задачи или снимает его.")]
    public Task<McpTask> AssignTask(
        [Description("Код задачи или Id.")] string task,
        [Description("username, «me» или «none» — снять исполнителя.")] string assignee,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var userId = await flow.ResolveUserAsync(assignee, cancellationToken);

            var result = await flow.Mediator.Send(new TaskAssignCommand(flow.ActorId, found.Id, userId), cancellationToken);
            if (result.IsNotFound)
                throw new McpException($"Задача «{task}» не найдена.");
            if (result.ValidationError is { } error)
                throw new McpException(error);

            return await flow.ToViewAsync(result.Response!, withDescription: false, cancellationToken);
        });

    [McpServerTool(Name = "set_due_date", Title = "Срок задачи", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Ставит или снимает срок задачи.")]
    public Task<McpTask> SetDueDate(
        [Description("Код задачи или Id.")] string task,
        [Description("Дата в формате yyyy-MM-dd; пусто — снять срок.")] string? dueDate,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            DateOnly? date = null;
            if (!string.IsNullOrWhiteSpace(dueDate))
            {
                if (!DateOnly.TryParseExact(dueDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                    throw new McpException($"Дата «{dueDate}» не в формате yyyy-MM-dd.");
                date = parsed;
            }

            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var result = await flow.Mediator.Send(new TaskSetDueDateCommand(flow.ActorId, found.Id, date), cancellationToken);
            return await UpdatedAsync(task, result, cancellationToken);
        });

    [McpServerTool(Name = "add_comment", Title = "Комментарий", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Добавляет комментарий к задаче (Markdown, @username — упоминание).")]
    public Task<McpComment> AddComment(
        [Description("Код задачи или Id.")] string task,
        [Description("Текст комментария в Markdown.")] string body,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var result = await flow.Mediator.Send(new TaskCommentAddCommand(flow.ActorId, found.Id, body), cancellationToken);
            var comment = result.Response ?? throw new McpException($"Задача «{task}» не найдена.");
            return new McpComment(comment.Id, await flow.UsernameAsync(comment.AuthorId, cancellationToken), comment.Body, comment.CreatedAt, comment.EditedAt);
        });

    [McpServerTool(Name = "add_checklist_item", Title = "Пункт чек-листа", Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Добавляет пункт в чек-лист задачи. Возвращает чек-лист целиком.")]
    public Task<IReadOnlyList<McpChecklistItem>> AddChecklistItem(
        [Description("Код задачи или Id.")] string task,
        [Description("Текст пункта.")] string text,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var items = await flow.Mediator.Send(new TaskChecklistAddCommand(flow.ActorId, found.Id, text), cancellationToken);
            return Checklist(task, items);
        });

    [McpServerTool(Name = "update_checklist_item", Title = "Отметить пункт чек-листа", Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Отмечает пункт чек-листа выполненным (или снимает отметку) и/или меняет его текст. Id пункта — из get_task.")]
    public Task<IReadOnlyList<McpChecklistItem>> UpdateChecklistItem(
        [Description("Код задачи или Id.")] string task,
        [Description("Id пункта чек-листа.")] Guid itemId,
        [Description("Выполнен ли пункт.")] bool? done = null,
        [Description("Новый текст пункта.")] string? text = null,
        CancellationToken cancellationToken = default) =>
        flow.RunAsync(async () =>
        {
            var found = await flow.ResolveTaskAsync(task, cancellationToken);
            var items = await flow.Mediator.Send(new TaskChecklistUpdateCommand(flow.ActorId, found.Id, itemId, text, done), cancellationToken);
            return Checklist(task, items);
        });

    private async Task<McpTask> UpdatedAsync(string task, TaskUpdateResult result, CancellationToken cancellationToken)
    {
        if (result.IsNotFound)
            throw new McpException($"Задача «{task}» не найдена.");
        if (result.ValidationError is { } error)
            throw new McpException(error);

        return await flow.ToViewAsync(result.Response!, withDescription: false, cancellationToken);
    }

    private static IReadOnlyList<McpChecklistItem> Checklist(string task, IReadOnlyList<TaskChecklistItemResponse>? items) =>
        items?.Select(i => new McpChecklistItem(i.Id, i.Text, i.IsDone)).ToList()
        ?? throw new McpException($"Задача «{task}» или пункт чек-листа не найдены.");
}
