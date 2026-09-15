using Flow.Application.Abstractions;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchStatusQuery;
using Flow.Shared.Contracts.Search;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Эксплуатация поискового индекса. Самого поиска (GET /search) здесь пока нет — он появится
/// вместе с гибридным запросом; сейчас эндпоинты нужны, чтобы видеть состояние индекса и наполнять его.
/// </summary>
[ApiController]
[Route("search")]
public class SearchController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Состояние индекса: очередь, застрявшие записи, чанки по типам, версия модели. Admin и Owner.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await mediator.Send(new SearchStatusQuery(actor.Require()), cancellationToken);
        return Ok(status);
    }

    /// <summary>
    /// Ставит источники в очередь переиндексации и сразу отвечает 202: чанки построит фоновый воркер.
    /// Только Owner. 400 — поиск выключен.
    /// </summary>
    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(ReindexRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var enqueued = await mediator.Send(
                new ReindexCommand(actor.Require(), request?.Types, request?.BoardId),
                cancellationToken);

            return Accepted(new { Enqueued = enqueued });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
