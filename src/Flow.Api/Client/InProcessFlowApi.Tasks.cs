using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkDeleteCommand;
using Flow.Application.Features.Tasks.Queries.TaskChecklistQuery;
using Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetParentCommand;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>
/// Задачи: те же исходы, что у TasksController. И запросы, и команды берут actor через ActorAsync внутри Guard:
/// запросам он нужен для фильтра приватных проектов (docs/TZ_project_access.md, 4B), а протухшая сессия
/// стала 401, а не необработанным исключением в середине рендера.
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid boardId, Guid? assigneeId = null, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            // GET boards/{boardId}/tasks всегда 200: несуществующая доска даёт пустой список, а не 404.
            var tasks = await mediator.Send(new TaskListQuery(await ActorAsync(), boardId, assigneeId), ct);
            return Ok(tasks);
        });

    /// <summary>
    /// Сводный список задач: boardId = null — по всем проектам. Фильтры и счётчики считает Flow.Application,
    /// поэтому первая страница уже несёт верные итоги по типам статусов.
    /// </summary>
    public Task<ApiResult<TaskListResponse>> SearchTasks(
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        Guid? statusId = null,
        StatusType? statusType = null,
        string? query = null,
        int? limit = null,
        string? cursor = null,
        int? offset = null,
        TaskSortField? sort = null,
        bool descending = false,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        Guid? parentId = null,
        string? fql = null,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var sortField = sort ?? TaskSortField.Created;

            // Повторяем разбор dir из контроллера: HTTP-клиент клал dir в строку запроса только вместе
            // с sort, поэтому при sort = null флаг вызывающего не доезжал и список шёл «новые сверху».
            // Без этой ветки страницы, зовущие SearchTasks без сортировки, внезапно получили бы старые сверху.
            var sortDescending = sort is null ? sortField == TaskSortField.Created : descending;

            // Пустые строки отбрасываем здесь, как раньше это делал сборщик query-string: иначе
            // q = "   " дошёл бы до поиска как значимый фильтр и вернул пустую страницу.
            var text = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            var page = string.IsNullOrWhiteSpace(cursor) ? null : cursor;

            var response = await mediator.Send(
                new TaskSearchQuery(await ActorAsync(), boardId, assigneeId, unassigned, statusId, statusType, text, limit, page,
                    offset, sortField, sortDescending, typeKind, priority, parentId, string.IsNullOrWhiteSpace(fql) ? null : fql),
                ct);

            return Ok(response);
        });

    public Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var task = await mediator.Send(new TaskGetQuery(await ActorAsync(), id), ct);
            return task is null ? NotFound<TaskResponse>() : Ok(task);
        });

    public Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new TaskCreateCommand(actor, boardId, request.Title, request.Description, request.StatusId,
                    request.TypeId, request.Priority?.ToDomainPriority(), request.ParentId),
                ct);

            // null — доски нет; ArgumentException/InvalidOperationException (пустой заголовок, чужой статус)
            // ловит Guard и отдаёт 400, как делал catch в контроллере.
            return response is null ? NotFound<TaskResponse>() : Ok(response);
        });

    public Task<ApiResult<TaskResponse>> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(
                new TaskUpdateCommand(actor, id, request.Title, request.Description, request.StatusId,
                    request.TypeId, request.Priority?.ToDomainPriority()),
                ct);

            if (result.IsNotFound)
                return NotFound<TaskResponse>();

            return result.ValidationError is { } error
                ? Invalid<TaskResponse>(error)
                : Ok(result.Response!);
        });

    /// <summary>cascade = false и есть подзадачи — 400 (Guard ловит InvalidOperationException).</summary>
    public Task<ApiResult<bool>> DeleteTask(Guid id, bool cascade = false, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var deleted = await mediator.Send(new TaskDeleteCommand(actor, id, cascade), ct);

            // Удаление уже удалённой задачи для экрана не ошибка: HTTP-клиент на 404 отдавал Success(false).
            return deleted ? Ok(true) : Missing();
        });

    /// <summary>UserId = null в запросе — снять исполнителя. Неизвестный или деактивированный пользователь → 400.</summary>
    public Task<ApiResult<TaskResponse>> AssignTask(Guid id, AssignTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new TaskAssignCommand(actor, id, request.UserId), ct);

            if (result.IsNotFound)
                return NotFound<TaskResponse>();

            return result.ValidationError is { } error
                ? Invalid<TaskResponse>(error)
                : Ok(result.Response!);
        });

    /// <summary>DueDate = null в запросе — снять срок. Срок раньше даты начала — ArgumentException, Guard отдаёт 400.</summary>
    public Task<ApiResult<TaskResponse>> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetDueDateCommand(actor, id, request.DueDate), ct);

    public Task<ApiResult<TaskResponse>> SetSchedule(Guid id, SetTaskScheduleRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetScheduleCommand(actor, id, request.StartDate, request.DueDate), ct);

    public Task<ApiResult<TaskResponse>> SetEstimate(Guid id, SetTaskEstimateRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetEstimateCommand(actor, id, request.StoryPoints, request.EstimateMinutes), ct);

    public Task<ApiResult<TaskResponse>> SetParent(Guid id, SetTaskParentRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetParentCommand(actor, id, request.ParentId), ct);

    public Task<ApiResult<TaskResponse>> RankTask(Guid id, RankTaskRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskRankCommand(actor, id, request.AfterId, request.BeforeId, request.StatusId), ct);

    public Task<ApiResult<IReadOnlyList<TaskTreeNode>>> GetTree(Guid boardId, Guid? rootId = null, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var tree = await mediator.Send(new TaskTreeQuery(await ActorAsync(), boardId, rootId), ct);
            return tree is null ? NotFound<IReadOnlyList<TaskTreeNode>>() : Ok(tree);
        });

    public Task<ApiResult<IReadOnlyList<TaskLinkResponse>>> GetLinks(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var links = await mediator.Send(new TaskLinkListQuery(await ActorAsync(), taskId), ct);
            return links is null ? NotFound<IReadOnlyList<TaskLinkResponse>>() : Ok(links);
        });

    /// <summary>409 — такая связь уже есть; 400 (Guard) — вторая задача не найдена или скрыта.</summary>
    public Task<ApiResult<TaskLinkCreatedResponse>> CreateLink(Guid taskId, CreateTaskLinkRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var result = await mediator.Send(
                new TaskLinkCreateCommand(await ActorAsync(), taskId, request.Type.ToDomainLinkType(), request.TargetId, request.TargetCode, request.Inward), ct);

            if (result.IsNotFound)
                return NotFound<TaskLinkCreatedResponse>();
            return result.IsDuplicate
                ? Conflict<TaskLinkCreatedResponse>("Такая связь уже есть")
                : Ok(result.Response!);
        });

    public Task<ApiResult<bool>> DeleteLink(Guid linkId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskLinkDeleteCommand(await ActorAsync(), linkId), ct) ? Ok(true) : Missing());

    public Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> GetChecklist(Guid taskId, CancellationToken ct = default) =>
        SendChecklist(actor => new TaskChecklistQuery(actor, taskId), ct);

    public Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> AddChecklistItem(Guid taskId, AddChecklistItemRequest request, CancellationToken ct = default) =>
        SendChecklist(actor => new TaskChecklistAddCommand(actor, taskId, request.Text), ct);

    public Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> UpdateChecklistItem(Guid taskId, Guid itemId, UpdateChecklistItemRequest request, CancellationToken ct = default) =>
        SendChecklist(actor => new TaskChecklistUpdateCommand(actor, taskId, itemId, request.Text, request.IsDone), ct);

    public Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> DeleteChecklistItem(Guid taskId, Guid itemId, CancellationToken ct = default) =>
        SendChecklist(actor => new TaskChecklistDeleteCommand(actor, taskId, itemId), ct);

    public Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> ReorderChecklist(Guid taskId, ReorderChecklistRequest request, CancellationToken ct = default) =>
        SendChecklist(actor => new TaskChecklistReorderCommand(actor, taskId, request.ItemIds), ct);

    private Task<ApiResult<IReadOnlyList<TaskChecklistItemResponse>>> SendChecklist(
        Func<Guid, MediatR.IRequest<IReadOnlyList<TaskChecklistItemResponse>?>> request, CancellationToken ct) =>
        Scoped(async mediator =>
        {
            var items = await mediator.Send(request(await ActorAsync()), ct);
            return items is null ? NotFound<IReadOnlyList<TaskChecklistItemResponse>>() : Ok(items);
        });

    /// <summary>Команды TaskUpdateResult: «задачи нет», отказ (статус не того проекта, workflow) и успех; ошибки ввода домена ловит Guard (400).</summary>
    private Task<ApiResult<TaskResponse>> SendUpdate(Func<Guid, MediatR.IRequest<TaskUpdateResult>> command, CancellationToken ct) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(command(actor), ct);
            if (result.IsNotFound)
                return NotFound<TaskResponse>();

            // Перенос в колонку канбана проходит workflow: отказ приходит ошибкой ввода с причинами в тексте.
            return result.ValidationError is { } error ? Invalid<TaskResponse>(error) : Ok(result.Response!);
        });
}
