using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
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
    [ProducesResponseType<BoardResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiError>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateBoard(CreateBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(
                new BoardCreateCommand(actor.Require(), request.Name, request.Key),
                cancellationToken);

            if (result.IsKeyTaken)
                return Conflict(new ApiError(result.ValidationError!));

            var response = result.Response!;
            return CreatedAtAction(nameof(GetBoard), new { id = response.Id }, response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ApiError(ex.Message));
        }
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BoardResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBoards(CancellationToken cancellationToken)
    {
        var boards = await mediator.Send(new BoardListQuery(), cancellationToken);
        return Ok(boards);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<BoardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBoard(Guid id, CancellationToken cancellationToken)
    {
        var board = await mediator.Send(new BoardGetQuery(id), cancellationToken);
        return board is null ? NotFound() : Ok(board);
    }

    [HttpPatch("{id:guid}/name")]
    [ProducesResponseType<BoardResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiError>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
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
            return BadRequest(new ApiError(ex.Message));
        }
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiError>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteBoard(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new BoardDeleteCommand(actor.Require(), id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
