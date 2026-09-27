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
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Search.Queries.SimilarTasksQuery;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Tasks.Queries.TaskBoardQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Features.Boards.Workflow;
using Flow.Shared.Contracts.Filters;
using Flow.Shared.Contracts.Boards;
using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Tasks;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Маршруты у задач разбиты на два префикса ("boards/{boardId}/tasks" для создания/списка в рамках доски
/// и "tasks/{id}" для операций над конкретной задачей), поэтому у контроллера нет общего [Route("[controller]")] —
/// каждый action задаёт свой полный путь явно.
/// </summary>
[ApiController]
public class TasksController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpPost("boards/{boardId:guid}/tasks")]
    public async Task<IActionResult> CreateTask(Guid boardId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new TaskCreateCommand(actor.Require(), boardId, request.Title, request.Description, request.StatusId,
                    request.TypeId, request.Priority?.ToDomainPriority(), request.ParentId, request.CustomFields, request.AssigneeId),
                cancellationToken);

            return response is null
                ? NotFound()
                : CreatedAtAction(nameof(GetTask), new { id = response.Id }, response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet("boards/{boardId:guid}/tasks")]
    public async Task<IActionResult> GetBoardTasks(Guid boardId, [FromQuery] Guid? assigneeId, CancellationToken cancellationToken)
    {
        var tasks = await mediator.Send(new TaskListQuery(actor.Require(), boardId, assigneeId), cancellationToken);
        return Ok(tasks);
    }

    /// <summary>
    /// Сводный список задач: без boardId — по всем проектам, с boardId — по одному.
    /// Страница отдаётся вместе со счётчиками по типам статусов и курсором следующей страницы.
    /// Листать можно двумя способами: cursor (кнопка «показать ещё») или offset (таблица со страницами);
    /// сортировку задают sort и dir, и она применима только к offset-режиму — курсор кодирует
    /// порядок по умолчанию (новые сверху).
    /// </summary>
    [HttpGet("tasks")]
    public async Task<IActionResult> SearchTasks(
        [FromQuery] Guid? boardId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool? unassigned,
        [FromQuery] Guid? statusId,
        [FromQuery] StatusType? statusType,
        [FromQuery] string? q,
        [FromQuery] int? limit,
        [FromQuery] string? cursor,
        [FromQuery] int? offset,
        [FromQuery] TaskSortField? sort,
        [FromQuery] string? dir,
        [FromQuery] TaskTypeKind? typeKind,
        [FromQuery] TaskPriority? priority,
        [FromQuery] Guid? parentId,
        [FromQuery] string? fql,
        CancellationToken cancellationToken)
    {
        // Неизвестное число в query-string («?priority=42») привязка enum'а пропускает — отсекаем до маппинга.
        if (typeKind is { } kind && !Enum.IsDefined(kind))
            return BadRequest(new { Message = $"Unknown task type kind {kind}." });
        if (priority is { } p && !Enum.IsDefined(p))
            return BadRequest(new { Message = $"Unknown priority {p}." });

        var sortField = sort ?? TaskSortField.Created;
        // По умолчанию новые сверху, для остальных колонок — по возрастанию: так ожидают от списка.
        var descending = dir is null
            ? sortField == TaskSortField.Created
            : string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        try
        {
            var response = await mediator.Send(
                new TaskSearchQuery(actor.Require(), boardId, assigneeId, unassigned == true, statusId, statusType, q, limit, cursor,
                    offset, sortField, descending, typeKind, priority, parentId, fql),
                cancellationToken);

            return Ok(response);
        }
        catch (FqlException ex)
        {
            // Место ошибки — чтобы клиент подчеркнул его в строке запроса.
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
    }

    [HttpGet("tasks/{id:guid}")]
    public async Task<IActionResult> GetTask(Guid id, CancellationToken cancellationToken)
    {
        var task = await mediator.Send(new TaskGetQuery(actor.Require(), id), cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPatch("tasks/{id:guid}")]
    public async Task<IActionResult> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new TaskUpdateCommand(actor.Require(), id, request.Title, request.Description, request.StatusId,
                    request.TypeId, request.Priority?.ToDomainPriority()),
                cancellationToken);

            if (result.IsNotFound)
                return NotFound();

            // Workflow не пустил: 400, а не 409 — конфликта версий нет, это правило (docs/TZ_workflow_config.md §2).
            if (result.Reasons is { } reasons)
                return BadRequest(new { Message = result.ValidationError, Reasons = reasons });

            if (result.ValidationError is not null)
                return BadRequest(new { Message = result.ValidationError });

            return Ok(result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // InvalidOperationException — архивный тип задачи (TaskItem.ChangeType).
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>UserId = null в теле — снять исполнителя. Неизвестный или деактивированный пользователь → 400.</summary>
    [HttpPatch("tasks/{id:guid}/assignee")]
    public async Task<IActionResult> AssignTask(Guid id, AssignTaskRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new TaskAssignCommand(actor.Require(), id, request.UserId), cancellationToken);

        if (result.IsNotFound)
            return NotFound();

        if (result.ValidationError is not null)
            return BadRequest(new { Message = result.ValidationError });

        return Ok(result.Response);
    }

    /// <summary>DueDate = null в теле — снять срок. Срок раньше даты начала → 400.</summary>
    [HttpPatch("tasks/{id:guid}/due-date")]
    public Task<IActionResult> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken cancellationToken) =>
        SendUpdate(new TaskSetDueDateCommand(actor.Require(), id, request.DueDate), cancellationToken);

    /// <summary>Дата начала и срок вместе (null — снять); начало позже срока → 400.</summary>
    [HttpPatch("tasks/{id:guid}/schedule")]
    public Task<IActionResult> SetSchedule(Guid id, SetTaskScheduleRequest request, CancellationToken cancellationToken) =>
        SendUpdate(new TaskSetScheduleCommand(actor.Require(), id, request.StartDate, request.DueDate), cancellationToken);

    /// <summary>Story points и оценка в минутах вместе (null — снять); вне диапазона → 400.</summary>
    [HttpPatch("tasks/{id:guid}/estimate")]
    public Task<IActionResult> SetEstimate(Guid id, SetTaskEstimateRequest request, CancellationToken cancellationToken) =>
        SendUpdate(new TaskSetEstimateCommand(actor.Require(), id, request.StoryPoints, request.EstimateMinutes), cancellationToken);

    /// <summary>Родитель в иерархии (null — снять); другой проект, свой уровень или ниже → 400.</summary>
    [HttpPatch("tasks/{id:guid}/parent")]
    public Task<IActionResult> SetParent(Guid id, SetTaskParentRequest request, CancellationToken cancellationToken) =>
        SendUpdate(new TaskSetParentCommand(actor.Require(), id, request.ParentId), cancellationToken);

    /// <summary>Место в ручном порядке: после afterId и/или перед beforeId; без соседей или соседи из другого проекта → 400.</summary>
    [HttpPost("tasks/{id:guid}/rank")]
    public Task<IActionResult> RankTask(Guid id, RankTaskRequest request, CancellationToken cancellationToken) =>
        SendUpdate(new TaskRankCommand(actor.Require(), id, request.AfterId, request.BeforeId, request.StatusId, request.SprintId, request.ToBacklog), cancellationToken);

    /// <summary>
    /// Канбан (docs/TZ_task_views.md §1): boardId — колонки-статусы проекта, без него — виды статусов по всем проектам.
    /// statusId / statusType / other с offset — одна колонка со следующей страницы (догрузка при прокрутке).
    /// </summary>
    [HttpGet("tasks/board")]
    public async Task<IActionResult> GetTaskBoard(
        [FromQuery] Guid? boardId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool? unassigned,
        [FromQuery] string? q,
        [FromQuery] TaskTypeKind? typeKind,
        [FromQuery] TaskPriority? priority,
        [FromQuery] string? fql,
        [FromQuery] Guid? statusId,
        [FromQuery] StatusType? statusType,
        [FromQuery] bool? other,
        [FromQuery] int? offset,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (typeKind is { } kind && !Enum.IsDefined(kind))
            return BadRequest(new { Message = $"Unknown task type kind {kind}." });
        if (priority is { } p && !Enum.IsDefined(p))
            return BadRequest(new { Message = $"Unknown priority {p}." });
        if (statusType is { } st && !Enum.IsDefined(st))
            return BadRequest(new { Message = $"Unknown status type {st}." });

        try
        {
            var response = await mediator.Send(
                new TaskBoardQuery(actor.Require(), boardId, assigneeId, unassigned == true, q, typeKind, priority, fql,
                    statusId, statusType, other == true, offset ?? 0, limit),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (FqlException ex)
        {
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Куда можно перевести задачу по workflow проекта: по каждому статусу — можно ли и почему нет.</summary>
    [HttpGet("tasks/{id:guid}/transitions")]
    public async Task<IActionResult> GetTransitions(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskTransitionsQuery(actor.Require(), id), cancellationToken) is { } transitions ? Ok(transitions) : NotFound();

    /// <summary>Подсказки FQL у курсора: q — строка, pos — позиция курсора (по умолчанию конец).</summary>
    [HttpGet("tasks/query/suggest")]
    public async Task<IActionResult> SuggestQuery([FromQuery] string? q, [FromQuery] int? pos, CancellationToken cancellationToken)
    {
        var query = q ?? "";
        return Ok(await mediator.Send(new FqlSuggestQuery(actor.Require(), query, pos ?? query.Length), cancellationToken));
    }

    // ---- Связи (docs/TZ_task_model.md §5) ----

    [HttpGet("tasks/{id:guid}/links")]
    public async Task<IActionResult> GetLinks(Guid id, CancellationToken cancellationToken)
    {
        var links = await mediator.Send(new TaskLinkListQuery(actor.Require(), id), cancellationToken);
        return links is null ? NotFound() : Ok(links);
    }

    /// <summary>201 — связь создана (cycleWarning — цикл блокировок); 400 — вторая задача не найдена или скрыта, на себя; 409 — такая уже есть.</summary>
    [HttpPost("tasks/{id:guid}/links")]
    public async Task<IActionResult> CreateLink(Guid id, CreateTaskLinkRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new TaskLinkCreateCommand(actor.Require(), id, request.Type.ToDomainLinkType(), request.TargetId, request.TargetCode, request.Inward),
                cancellationToken);

            if (result.IsNotFound)
                return NotFound();
            if (result.IsDuplicate)
                return Conflict(new { Message = "Such a link already exists." });

            return CreatedAtAction(nameof(GetLinks), new { id }, result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("links/{id:guid}")]
    public async Task<IActionResult> DeleteLink(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskLinkDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    // ---- Чек-лист (§8): ответ — чек-лист целиком ----

    [HttpGet("tasks/{id:guid}/checklist")]
    public Task<IActionResult> GetChecklist(Guid id, CancellationToken cancellationToken) =>
        SendChecklist(new TaskChecklistQuery(actor.Require(), id), cancellationToken);

    [HttpPost("tasks/{id:guid}/checklist")]
    public Task<IActionResult> AddChecklistItem(Guid id, AddChecklistItemRequest request, CancellationToken cancellationToken) =>
        SendChecklist(new TaskChecklistAddCommand(actor.Require(), id, request.Text), cancellationToken);

    [HttpPatch("tasks/{id:guid}/checklist/{itemId:guid}")]
    public Task<IActionResult> UpdateChecklistItem(Guid id, Guid itemId, UpdateChecklistItemRequest request, CancellationToken cancellationToken) =>
        SendChecklist(new TaskChecklistUpdateCommand(actor.Require(), id, itemId, request.Text, request.IsDone), cancellationToken);

    [HttpDelete("tasks/{id:guid}/checklist/{itemId:guid}")]
    public Task<IActionResult> DeleteChecklistItem(Guid id, Guid itemId, CancellationToken cancellationToken) =>
        SendChecklist(new TaskChecklistDeleteCommand(actor.Require(), id, itemId), cancellationToken);

    [HttpPut("tasks/{id:guid}/checklist/order")]
    public Task<IActionResult> ReorderChecklist(Guid id, ReorderChecklistRequest request, CancellationToken cancellationToken) =>
        SendChecklist(new TaskChecklistReorderCommand(actor.Require(), id, request.ItemIds), cancellationToken);

    private async Task<IActionResult> SendChecklist(MediatR.IRequest<IReadOnlyList<TaskChecklistItemResponse>?> request, CancellationToken cancellationToken)
    {
        try
        {
            var items = await mediator.Send(request, cancellationToken);
            return items is null ? NotFound() : Ok(items);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// Дерево задач проекта или поддерево rootId: плоский список в порядке обхода с глубиной (docs/TZ_task_views.md §3).
    /// Фильтры — как у GET /tasks, предки подходящих задач приходят с isContextOnly; maxDepth — глубина от корня обхода.
    /// </summary>
    [HttpGet("boards/{boardId:guid}/tree")]
    public async Task<IActionResult> GetTree(
        Guid boardId,
        [FromQuery] Guid? rootId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool? unassigned,
        [FromQuery] string? q,
        [FromQuery] TaskTypeKind? typeKind,
        [FromQuery] TaskPriority? priority,
        [FromQuery] Guid? statusId,
        [FromQuery] string? fql,
        [FromQuery] int? maxDepth,
        CancellationToken cancellationToken)
    {
        if (typeKind is { } kind && !Enum.IsDefined(kind))
            return BadRequest(new { Message = $"Unknown task type kind {kind}." });
        if (priority is { } p && !Enum.IsDefined(p))
            return BadRequest(new { Message = $"Unknown priority {p}." });
        if (maxDepth is < 0)
            return BadRequest(new { Message = "maxDepth must not be negative." });

        try
        {
            var tree = await mediator.Send(
                new TaskTreeQuery(actor.Require(), boardId, rootId, assigneeId, unassigned == true, q, typeKind, priority, statusId, fql, maxDepth),
                cancellationToken);
            return tree is null ? NotFound() : Ok(tree);
        }
        catch (FqlException ex)
        {
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
    }

    private async Task<IActionResult> SendUpdate(MediatR.IRequest<TaskUpdateResult> command, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(command, cancellationToken);
            if (result.IsNotFound)
                return NotFound();

            // Перенос в колонку канбана проходит workflow: отказ — 400 с причинами, как у PATCH /tasks/{id}.
            if (result.ValidationError is { } error)
                return BadRequest(result.Reasons is { } reasons ? new { Message = error, Reasons = reasons } : new { Message = error });

            return Ok(result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Задача с подзадачами без cascade=true → 400; с флагом удаляется всё поддерево.</summary>
    [HttpDelete("tasks/{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid id, [FromQuery] bool? cascade, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await mediator.Send(new TaskDeleteCommand(actor.Require(), id, cascade == true), cancellationToken);
            return deleted ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// Похожие задачи по вектору этой задачи (без повторного инференса), исключая её саму.
    /// Читать может любая роль. 404 — задачи нет или поиск выключен.
    /// </summary>
    [HttpGet("tasks/{id:guid}/similar")]
    public async Task<IActionResult> GetSimilar(Guid id, CancellationToken cancellationToken, [FromQuery] int limit = 5)
    {
        var similar = await mediator.Send(new SimilarTasksQuery(actor.Require(), id, limit), cancellationToken);
        return similar is null ? NotFound() : Ok(similar);
    }
}
