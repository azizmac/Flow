using Flow.Application.Abstractions;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchStatusQuery;
using Flow.Shared.Contracts.Search;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Служебные ручки поиска. Самого поиска (GET /search) здесь пока нет — он появится вместе с гибридной
/// выдачей; сейчас наружу торчит только то, чем чинят индексацию.
/// [Authorize] стоит явно, хотя в приложении и так fallback-политика: эти ручки видят внутренности
/// системы, и их доступность не должна зависеть от настройки где-то в Program.cs.
/// </summary>
[ApiController]
[Authorize]
[Route("search")]
public class SearchController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>Состояние поиска: жив ли эмбеддер, что лежит в индексе, не застряла ли очередь. Admin и Owner.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var status = await mediator.Send(new SearchStatusQuery(actor.Require()), cancellationToken);
        return Ok(status);
    }

    /// <summary>
    /// Массовая переиндексация. 202, а не 200: команда только наполняет очередь, векторы считает воркер.
    /// Только Owner.
    /// </summary>
    [HttpPost("reindex")]
    public async Task<IActionResult> Reindex(ReindexRequest? request, CancellationToken cancellationToken)
    {
        try
        {
            var queued = await mediator.Send(
                new ReindexCommand(actor.Require(), ToSourceTypes(request?.Types), request?.BoardId),
                cancellationToken);

            return Accepted(new ReindexResponse(queued));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>Значения enum'ов совпадают, но полагаться на это молча не стоит — маппим явно.</summary>
    private static IReadOnlyList<SearchSourceType>? ToSourceTypes(IReadOnlyList<SearchSourceKind>? kinds) =>
        kinds?.Select(kind => kind switch
        {
            SearchSourceKind.Task => SearchSourceType.Task,
            SearchSourceKind.Comment => SearchSourceType.Comment,
            SearchSourceKind.Board => SearchSourceType.Board,
            SearchSourceKind.User => SearchSourceType.User,
            SearchSourceKind.Attachment => SearchSourceType.Attachment,
            _ => throw new ArgumentException($"Неизвестный тип источника: {kind}.", nameof(kinds))
        }).ToList();
}
