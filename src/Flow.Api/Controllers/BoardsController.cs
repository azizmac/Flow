using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Abstractions;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

[ApiController]
[Route("boards")]
public class BoardsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateBoard(CreateBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new BoardCreateCommand(actor.Require(), request.Name, request.Key),
                cancellationToken);

            if (result.IsKeyTaken)
                return Conflict(new { Message = result.ValidationError });

            var response = result.Response!;
            return CreatedAtAction(nameof(GetBoard), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBoards(CancellationToken cancellationToken)
    {
        var boards = await mediator.Send(new BoardListQuery(), cancellationToken);
        return Ok(boards);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetBoard(Guid id, CancellationToken cancellationToken)
    {
        var board = await mediator.Send(new BoardGetQuery(id), cancellationToken);
        return board is null ? NotFound() : Ok(board);
    }

    [HttpPatch("{id:guid}/name")]
    public async Task<IActionResult> RenameBoard(Guid id, RenameBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new BoardRenameCommand(actor.Require(), id, request.Name),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Добавить тип задачи; ответ — проект целиком (флаг «по умолчанию» мог переехать). 400 — занятое имя.</summary>
    [HttpPost("{id:guid}/task-types")]
    public async Task<IActionResult> CreateTaskType(Guid id, CreateTaskTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!Enum.IsDefined(request.Kind))
                return BadRequest(new { Message = $"Unknown task type kind {request.Kind}." });

            var response = await mediator.Send(
                new TaskTypeCreateCommand(actor.Require(), id, request.Name, request.Kind.ToDomainKind(), request.IsDefault),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>PATCH типа: имя, «по умолчанию» (только true), архив. 400 — нарушение инварианта или чужой тип.</summary>
    [HttpPatch("{id:guid}/task-types/{typeId:guid}")]
    public async Task<IActionResult> UpdateTaskType(Guid id, Guid typeId, UpdateTaskTypeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new TaskTypeUpdateCommand(actor.Require(), id, typeId, request.Name, request.IsDefault, request.IsArchived),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBoard(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new BoardDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
