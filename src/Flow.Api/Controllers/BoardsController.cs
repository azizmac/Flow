using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Ids;
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
            var result = await mediator.Send(
                new BoardCreateCommand(request.Name, request.Key),
                cancellationToken);

            if (result.IsKeyTaken)
                return Conflict(new { Message = result.ValidationError });

            var response = result.Response!;
            return CreatedAtAction(nameof(GetBoard), new { id = response.Id.ToString() }, response);
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

    /// <summary>
    /// Id приходит строкой формата "boa_..." (маршрут без :guid-констрейнта) и парсится вручную —
    /// чужой префикс или битый Guid дают 400, а не 404.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetBoard(string id, CancellationToken cancellationToken)
    {
        if (!BoardId.TryParse(id, out var boardId))
            return InvalidId(id);

        var board = await mediator.Send(new BoardGetQuery(boardId), cancellationToken);
        return board is null ? NotFound() : Ok(board);
    }

    [HttpPatch("{id}/name")]
    public async Task<IActionResult> RenameBoard(string id, RenameBoardRequest request, CancellationToken cancellationToken)
    {
        if (!BoardId.TryParse(id, out var boardId))
            return InvalidId(id);

        try
        {
            var response = await mediator.Send(
                new BoardRenameCommand(boardId, request.Name),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteBoard(string id, CancellationToken cancellationToken)
    {
        if (!BoardId.TryParse(id, out var boardId))
            return InvalidId(id);

        var deleted = await mediator.Send(new BoardDeleteCommand(boardId), cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    private BadRequestObjectResult InvalidId(string id) =>
        new(new { Message = $"Invalid board id '{id}', expected format '{BoardId.Prefix}_<guid>'." });
}
