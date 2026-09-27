using Flow.Application.Abstractions;
using Flow.Application.Features.Boards.Commands.ScreenSetCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using DomainContext = Flow.Domain.Entities.ScreenContext;

namespace Flow.Api.Controllers;

/// <summary>
/// Экраны задач (docs/TZ_workflow_config.md §3). В адресе экрана — «default» (для всех типов) или Id типа задачи
/// и контекст create|detail. Раскладку для конкретной задачи клиент собирает сам из BoardResponse.Screens — тем же
/// правилом, что сервер (тип → «для всех типов» → встроенный), поэтому GET /tasks/{id}/screen не нужен.
/// </summary>
[ApiController]
[Route("boards/{boardId:guid}/screens")]
public class ScreensController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid boardId, CancellationToken cancellationToken) =>
        await mediator.Send(new BoardGetQuery(actor.Require(), boardId), cancellationToken) is { } board ? Ok(board.Screens ?? []) : NotFound();

    /// <summary>400 — неизвестное поле, дубль, поле, которого нет в форме создания, на экране create; чужой тип.</summary>
    [HttpPut("{type}/{context}")]
    public Task<IActionResult> Set(Guid boardId, string type, string context, SetScreenRequest request, CancellationToken cancellationToken) =>
        Send(type, context, (typeId, ctx) => new ScreenSetCommand(actor.Require(), boardId, typeId, ctx, request.Fields), cancellationToken);

    /// <summary>Снова действует экран «для всех типов» или встроенный.</summary>
    [HttpDelete("{type}/{context}")]
    public Task<IActionResult> Reset(Guid boardId, string type, string context, CancellationToken cancellationToken) =>
        Send(type, context, (typeId, ctx) => new ScreenResetCommand(actor.Require(), boardId, typeId, ctx), cancellationToken);

    private async Task<IActionResult> Send(string type, string context, Func<Guid?, DomainContext, IRequest<BoardResponse?>> command, CancellationToken cancellationToken)
    {
        Guid? typeId = type.Equals("default", StringComparison.OrdinalIgnoreCase) ? null
            : Guid.TryParse(type, out var parsed) ? parsed : Guid.Empty;
        if (typeId == Guid.Empty)
            return BadRequest(new { Message = "Тип экрана — «default» или Id типа задачи." });
        if (!Enum.TryParse<DomainContext>(context, ignoreCase: true, out var ctx) || !Enum.IsDefined(ctx))
            return BadRequest(new { Message = "Контекст экрана — create или detail." });

        try
        {
            return await mediator.Send(command(typeId, ctx), cancellationToken) is { } board ? Ok(board) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
