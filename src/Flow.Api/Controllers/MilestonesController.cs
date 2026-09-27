using Flow.Application.Abstractions;
using Flow.Application.Features.Milestones;
using Flow.Shared.Contracts.Milestones;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Вехи (docs/TZ_task_views.md §6): "boards/{id}/milestones" — по проекту, "milestones/{id}" — над конкретной вехой,
/// "tasks/{id}/milestone" — поле задачи. Задачи вехи — GET /tasks?fql=milestone = "…".
/// </summary>
[ApiController]
public class MilestonesController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("boards/{boardId:guid}/milestones")]
    public async Task<IActionResult> List(Guid boardId, CancellationToken cancellationToken) =>
        await mediator.Send(new MilestoneListQuery(actor.Require(), boardId), cancellationToken) is { } list ? Ok(list) : NotFound();

    [HttpGet("milestones/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new MilestoneGetQuery(actor.Require(), id), cancellationToken) is { } milestone ? Ok(milestone) : NotFound();

    /// <summary>400 — пустое или занятое в проекте имя.</summary>
    [HttpPost("boards/{boardId:guid}/milestones")]
    public Task<IActionResult> Create(Guid boardId, CreateMilestoneRequest request, CancellationToken cancellationToken) =>
        Send(new MilestoneCreateCommand(actor.Require(), boardId, request.Name, request.Description, request.TargetDate), cancellationToken);

    [HttpPatch("milestones/{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateMilestoneRequest request, CancellationToken cancellationToken) =>
        Send(new MilestoneUpdateCommand(actor.Require(), id, request.Name, request.Description, request.ClearDescription,
            request.TargetDate, request.ClearTargetDate, request.Closed), cancellationToken);

    /// <summary>Задачи вехи теряют её с записью в журнале.</summary>
    [HttpDelete("milestones/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new MilestoneDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    /// <summary>Веха задачи: milestoneId — веха её проекта, null — снять; 400 — закрытая или чужая веха.</summary>
    [HttpPatch("tasks/{id:guid}/milestone")]
    public async Task<IActionResult> SetTaskMilestone(Guid id, SetTaskMilestoneRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new TaskSetMilestoneCommand(actor.Require(), id, request.MilestoneId), cancellationToken);
            return result.IsNotFound ? NotFound() : Ok(result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    private async Task<IActionResult> Send(IRequest<MilestoneResponse?> command, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(command, cancellationToken) is { } milestone ? Ok(milestone) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
