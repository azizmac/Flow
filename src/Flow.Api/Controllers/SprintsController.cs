using Flow.Application.Abstractions;
using Flow.Application.Features.Sprints.Commands.SprintCompleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintDeleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintStartCommand;
using Flow.Application.Features.Sprints.Commands.SprintUpdateCommand;
using Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;
using Flow.Application.Features.Sprints.Queries.BacklogQuery;
using Flow.Application.Features.Sprints.Queries.SprintListQuery;
using Flow.Application.Features.Sprints.Queries.SprintReportQuery;
using Flow.Application.Features.Tasks.Fql;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Filters;
using Flow.Shared.Contracts.Sprints;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Спринты и бэклог (docs/TZ_task_views.md §2): "boards/{id}/sprints" и "boards/{id}/backlog" — по проекту,
/// "sprints/{id}" — над конкретным спринтом, "tasks/{id}/sprint" — поле задачи. Без общего [Route], как TasksController.
/// </summary>
[ApiController]
public class SprintsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("boards/{boardId:guid}/sprints")]
    public async Task<IActionResult> List(Guid boardId, CancellationToken cancellationToken) =>
        await mediator.Send(new SprintListQuery(actor.Require(), boardId), cancellationToken) is { } list ? Ok(list) : NotFound();

    [HttpPost("boards/{boardId:guid}/sprints")]
    public Task<IActionResult> Create(Guid boardId, CreateSprintRequest request, CancellationToken cancellationToken) =>
        Send(new SprintCreateCommand(actor.Require(), boardId, request.Name, request.Goal, request.StartDate, request.EndDate), cancellationToken);

    [HttpPatch("sprints/{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateSprintRequest request, CancellationToken cancellationToken) =>
        Send(new SprintUpdateCommand(actor.Require(), id, request.Name, request.Goal, request.ClearGoal, request.StartDate, request.EndDate, request.ClearDates), cancellationToken);

    /// <summary>400 — не запланированный, в проекте уже идёт другой, конец не позже начала.</summary>
    [HttpPost("sprints/{id:guid}/start")]
    public Task<IActionResult> Start(Guid id, StartSprintRequest request, CancellationToken cancellationToken) =>
        Send(new SprintStartCommand(actor.Require(), id, request.StartDate, request.EndDate), cancellationToken);

    /// <summary>moveOpenTo — спринт для незакрытых задач, null — бэклог; 400 — не активный или чужой спринт.</summary>
    [HttpPost("sprints/{id:guid}/complete")]
    public Task<IActionResult> Complete(Guid id, CompleteSprintRequest request, CancellationToken cancellationToken) =>
        Send(new SprintCompleteCommand(actor.Require(), id, request.MoveOpenTo), cancellationToken);

    /// <summary>Только запланированный (иначе 400); задачи уходят в бэклог.</summary>
    [HttpDelete("sprints/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new SprintDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet("sprints/{id:guid}/report")]
    public async Task<IActionResult> Report(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new SprintReportQuery(actor.Require(), id), cancellationToken) is { } report ? Ok(report) : NotFound();

    /// <summary>Бэклог проекта: секции спринтов и «Бэклог»; фильтры — как у GET /tasks, epicId — поддерево эпика.</summary>
    [HttpGet("boards/{boardId:guid}/backlog")]
    public async Task<IActionResult> Backlog(
        Guid boardId,
        [FromQuery] Guid? assigneeId,
        [FromQuery] bool? unassigned,
        [FromQuery] string? q,
        [FromQuery] TaskTypeKind? typeKind,
        [FromQuery] TaskPriority? priority,
        [FromQuery] string? fql,
        [FromQuery] Guid? epicId,
        CancellationToken cancellationToken)
    {
        if (typeKind is { } kind && !Enum.IsDefined(kind))
            return BadRequest(new { Message = $"Unknown task type kind {kind}." });
        if (priority is { } p && !Enum.IsDefined(p))
            return BadRequest(new { Message = $"Unknown priority {p}." });

        try
        {
            var backlog = await mediator.Send(
                new BacklogQuery(actor.Require(), boardId, assigneeId, unassigned == true, q, typeKind, priority, fql, epicId), cancellationToken);
            return backlog is null ? NotFound() : Ok(backlog);
        }
        catch (FqlException ex)
        {
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
    }

    /// <summary>Спринт задачи: sprintId — спринт её проекта, null — бэклог; 400 — завершённый или чужой спринт.</summary>
    [HttpPatch("tasks/{id:guid}/sprint")]
    public async Task<IActionResult> SetTaskSprint(Guid id, SetTaskSprintRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new TaskSetSprintCommand(actor.Require(), id, request.SprintId), cancellationToken);
            return result.IsNotFound ? NotFound() : Ok(result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    private async Task<IActionResult> Send(IRequest<SprintResponse?> command, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(command, cancellationToken) is { } sprint ? Ok(sprint) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
