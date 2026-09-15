using Flow.Application.Abstractions;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Search.Queries.SearchQuery;
using Flow.Application.Features.Search.Queries.SearchStatusQuery;
using Flow.Shared.Contracts.Search;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Поиск и эксплуатация поискового индекса: сама выдача, состояние очереди и переиндексация.
/// «Похожие задачи» и разбор фильтров прямо в строке запроса — следующий этап.
/// </summary>
[ApiController]
[Route("search")]
public class SearchController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    private const int DefaultLimit = 20;

    /// <summary>
    /// Единый поиск по задачам, комментариям, проектам и людям. Читать может любая роль.
    /// 404 — поиск выключен (Search:Enabled=false), 400 — пустой запрос или неизвестный тип источника.
    /// </summary>
    /// <param name="q">Строка запроса.</param>
    /// <param name="types">Типы источников через запятую: <c>task,comment,board,user</c>; пусто — все.</param>
    /// <param name="boardId">Искать только в одном проекте.</param>
    /// <param name="includeArchived">Включать задачи в финальном статусе; по умолчанию нет.</param>
    /// <param name="mode">hybrid (по умолчанию), semantic или text — для отладки качества.</param>
    [HttpGet]
    public async Task<IActionResult> Search(
        CancellationToken cancellationToken,
        [FromQuery] string? q = null,
        [FromQuery] string? types = null,
        [FromQuery] Guid? boardId = null,
        [FromQuery] bool includeArchived = false,
        [FromQuery] SearchMode mode = SearchMode.Hybrid,
        [FromQuery] int limit = DefaultLimit,
        [FromQuery] int offset = 0)
    {
        try
        {
            var response = await mediator.Send(
                new SearchQuery(actor.Require(), q ?? string.Empty, ParseTypes(types), boardId, includeArchived, mode, limit, offset),
                cancellationToken);

            return response is null ? NotFound() : Ok(response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    /// <summary>
    /// Типы источников из строки запроса: «task,comment» или повторяющиеся значения — оба варианта
    /// сводятся к одному списку. Неизвестное значение — ArgumentException (400), а не тихое игнорирование.
    /// </summary>
    private static IReadOnlyList<SearchSourceType>? ParseTypes(string? types)
    {
        if (string.IsNullOrWhiteSpace(types))
            return null;

        var parsed = new List<SearchSourceType>();
        foreach (var value in types.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<SearchSourceType>(value, ignoreCase: true, out var type) || type == SearchSourceType.Attachment)
                throw new ArgumentException($"Неизвестный тип источника: {value}.", nameof(types));

            parsed.Add(type);
        }

        return parsed;
    }

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
