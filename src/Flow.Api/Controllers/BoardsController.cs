using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

[ApiController]
[Route("boards")]
public class BoardsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> CreateBoard(CreateBoardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await mediator.Send(
                new BoardCreateCommand(request.Name, request.Key),
                cancellationToken);

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
                new BoardRenameCommand(id, request.Name),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteBoard(Guid id, CancellationToken cancellationToken)
    {
        var deleted = await mediator.Send(new BoardDeleteCommand(id), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
