using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Ids;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Маршруты у задач разбиты на два префикса ("boards/{boardId}/tasks" для создания/списка в рамках доски
/// и "tasks/{id}" для операций над конкретной задачей), поэтому у контроллера нет общего [Route("[controller]")] —
/// каждый action задаёт свой полный путь явно. Id приходят строками формата "boa_..."/"tas_..."
/// (маршруты без :guid-констрейнтов) и парсятся вручную; StatusId в теле запроса парсит
/// TypedIdJsonConverter — битый префикс там тоже даёт 400 силами [ApiController].
/// </summary>
[ApiController]
public class TasksController(IMediator mediator) : ControllerBase
{
    [HttpPost("boards/{boardId}/tasks")]
    public async Task<IActionResult> CreateTask(string boardId, CreateTaskRequest request, CancellationToken cancellationToken)
    {
        if (!BoardId.TryParse(boardId, out var parsedBoardId))
            return InvalidBoardId(boardId);

        try
        {
            var response = await mediator.Send(
                new TaskCreateCommand(parsedBoardId, request.Title, request.Description, request.StatusId),
                cancellationToken);

            return response is null
                ? NotFound()
                : CreatedAtAction(nameof(GetTask), new { id = response.Id.ToString() }, response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet("boards/{boardId}/tasks")]
    public async Task<IActionResult> GetBoardTasks(string boardId, CancellationToken cancellationToken)
    {
        if (!BoardId.TryParse(boardId, out var parsedBoardId))
            return InvalidBoardId(boardId);

        var tasks = await mediator.Send(new TaskListQuery(parsedBoardId), cancellationToken);
        return Ok(tasks);
    }

    [HttpGet("tasks/{id}")]
    public async Task<IActionResult> GetTask(string id, CancellationToken cancellationToken)
    {
        if (!TaskId.TryParse(id, out var taskId))
            return InvalidTaskId(id);

        var task = await mediator.Send(new TaskGetQuery(taskId), cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpPatch("tasks/{id}")]
    public async Task<IActionResult> UpdateTask(string id, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        if (!TaskId.TryParse(id, out var taskId))
            return InvalidTaskId(id);

        try
        {
            var result = await mediator.Send(
                new TaskUpdateCommand(taskId, request.Title, request.Description, request.StatusId),
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

    [HttpDelete("tasks/{id}")]
    public async Task<IActionResult> DeleteTask(string id, CancellationToken cancellationToken)
    {
        if (!TaskId.TryParse(id, out var taskId))
            return InvalidTaskId(id);

        var deleted = await mediator.Send(new TaskDeleteCommand(taskId), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private static BadRequestObjectResult InvalidBoardId(string boardId) =>
        new(new { Message = $"Invalid board id '{boardId}', expected format '{BoardId.Prefix}_<guid>'." });

    private static BadRequestObjectResult InvalidTaskId(string id) =>
        new(new { Message = $"Invalid task id '{id}', expected format '{TaskId.Prefix}_<guid>'." });
}
