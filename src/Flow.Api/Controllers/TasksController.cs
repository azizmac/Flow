using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Search.Queries.SimilarTasksQuery;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Shared.Contracts.Boards;
using Flow.Application.Abstractions;
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
                new TaskCreateCommand(actor.Require(), boardId, request.Title, request.Description, request.StatusId),
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
        var tasks = await mediator.Send(new TaskListQuery(boardId, assigneeId), cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var sortField = sort ?? TaskSortField.Created;
        // По умолчанию новые сверху, для остальных колонок — по возрастанию: так ожидают от списка.
        var descending = dir is null
            ? sortField == TaskSortField.Created
            : string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);

        var response = await mediator.Send(
            new TaskSearchQuery(boardId, assigneeId, unassigned == true, statusId, statusType, q, limit, cursor,
                offset, sortField, descending),
            cancellationToken);

        return Ok(response);
    }

    [HttpGet("tasks/{id:guid}")]
    public async Task<IActionResult> GetTask(Guid id, CancellationToken cancellationToken)
    {
        var task = await mediator.Send(new TaskGetQuery(id), cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPatch("tasks/{id:guid}")]
    public async Task<IActionResult> UpdateTask(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new TaskUpdateCommand(actor.Require(), id, request.Title, request.Description, request.StatusId),
                cancellationToken);

            if (result.IsNotFound)
                return NotFound();

            if (result.ValidationError is not null)
                return BadRequest(new { Message = result.ValidationError });

            return Ok(result.Response);
        }
        catch (ArgumentException ex)
        {
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

    /// <summary>DueDate = null в теле — снять срок.</summary>
    [HttpPatch("tasks/{id:guid}/due-date")]
    public async Task<IActionResult> SetDueDate(Guid id, SetTaskDueDateRequest request, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new TaskSetDueDateCommand(actor.Require(), id, request.DueDate), cancellationToken);
        return result.IsNotFound ? NotFound() : Ok(result.Response);
    }

    [HttpDelete("tasks/{id:guid}")]
    public async Task<IActionResult> DeleteTask(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new TaskDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
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
