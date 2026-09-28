using Flow.Application.Abstractions;
using Flow.Application.Features.TaskTemplates;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>Шаблоны задач (docs/TZ_workflow_config.md §5, этап 3G).</summary>
[ApiController]
public class TaskTemplatesController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("boards/{boardId:guid}/task-templates")]
    public async Task<IActionResult> List(Guid boardId, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskTemplateListQuery(actor.Require(), boardId), cancellationToken) is { } list ? Ok(list) : NotFound();

    [HttpPost("boards/{boardId:guid}/task-templates")]
    public Task<IActionResult> Create(Guid boardId, SaveTaskTemplateRequest request, CancellationToken cancellationToken) =>
        Created(() => mediator.Send(new TaskTemplateCreateCommand(actor.Require(), boardId, request), cancellationToken));

    [HttpPut("task-templates/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, SaveTaskTemplateRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new TaskTemplateUpdateCommand(actor.Require(), id, request), cancellationToken) is { } template ? Ok(template) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("task-templates/{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskTemplateDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpPost("tasks/{id:guid}/save-as-template")]
    public Task<IActionResult> FromTask(Guid id, TaskTemplateFromTaskRequest request, CancellationToken cancellationToken) =>
        Created(() => mediator.Send(new TaskTemplateFromTaskCommand(actor.Require(), id, request.Name), cancellationToken));

    private async Task<IActionResult> Created(Func<Task<TaskTemplateResponse?>> send)
    {
        try
        {
            return await send() is { } template ? StatusCode(StatusCodes.Status201Created, template) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
