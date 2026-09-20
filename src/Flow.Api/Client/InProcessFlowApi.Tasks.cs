using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>
/// Задачи: те же исходы, что у TasksController. Запросы (список, поиск, карточка) actor не получают —
/// читать может любая роль; команды берут его через ActorAsync внутри Guard, чтобы протухшая сессия
/// стала 401, а не необработанным исключением в середине рендера.
/// </summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<TaskResponse>>> GetTasks(Guid boardId, Guid? assigneeId = null, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            // GET boards/{boardId}/tasks всегда 200: несуществующая доска даёт пустой список, а не 404.
            var tasks = await mediator.Send(new TaskListQuery(boardId, assigneeId), ct);
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
                new TaskSearchQuery(boardId, assigneeId, unassigned, statusId, statusType, text, limit, page,
                    offset, sortField, sortDescending),
                ct);

            return Ok(response);
        });

    public Task<ApiResult<TaskResponse>> GetTask(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var task = await mediator.Send(new TaskGetQuery(id), ct);
            return task is null ? NotFound<TaskResponse>() : Ok(task);
        });

    public Task<ApiResult<TaskResponse>> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var response = await mediator.Send(
                new TaskCreateCommand(actor, boardId, request.Title, request.Description, request.StatusId),
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
                new TaskUpdateCommand(actor, id, request.Title, request.Description, request.StatusId),
                ct);

            if (result.IsNotFound)
                return NotFound<TaskResponse>();

            return result.ValidationError is { } error
                ? Invalid<TaskResponse>(error)
                : Ok(result.Response!);
        });

    public Task<ApiResult<bool>> DeleteTask(Guid id, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var deleted = await mediator.Send(new TaskDeleteCommand(actor, id), ct);

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

    /// <summary>DueDate = null в запросе — снять срок.</summary>
    public Task<ApiResult<TaskResponse>> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var actor = await ActorAsync();
            var result = await mediator.Send(new TaskSetDueDateCommand(actor, id, request.DueDate), ct);

            // Валидации у срока нет — команда различает только «задачи нет» и успех.
            return result.IsNotFound ? NotFound<TaskResponse>() : Ok(result.Response!);
        });
}
