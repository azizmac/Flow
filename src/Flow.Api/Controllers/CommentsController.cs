using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Лента задачи: комментарии ("tasks/{id}/comments" для списка/создания, "comments/{id}" для правки/удаления)
/// и журнал изменений ("tasks/{id}/activity"). Как и TasksController — без общего [Route], пути явные.
/// </summary>
[ApiController]
public class CommentsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> GetComments(Guid taskId, CancellationToken cancellationToken)
    {
        var comments = await mediator.Send(new TaskCommentListQuery(taskId), cancellationToken);
        return comments is null ? NotFound() : Ok(comments);
    }

    [HttpPost("tasks/{taskId:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid taskId, CreateTaskCommentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new TaskCommentAddCommand(actor.Require(), taskId, request.Body), cancellationToken);
            return result.IsNotFound
                ? NotFound()
                : CreatedAtAction(nameof(GetComments), new { taskId }, result.Response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("comments/{id:guid}")]
    public async Task<IActionResult> UpdateComment(Guid id, UpdateTaskCommentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new TaskCommentEditCommand(actor.Require(), id, request.Body), cancellationToken);
            return result.IsNotFound ? NotFound() : Ok(result.Response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("comments/{id:guid}")]
    public async Task<IActionResult> DeleteComment(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new TaskCommentDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("tasks/{taskId:guid}/activity")]
    public async Task<IActionResult> GetActivity(Guid taskId, CancellationToken cancellationToken)
    {
        var activity = await mediator.Send(new TaskActivityListQuery(taskId), cancellationToken);
        return activity is null ? NotFound() : Ok(activity);
    }
}
