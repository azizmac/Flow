using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Recurrence;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Повторение задачи (docs/TZ_task_model.md §9): правило на образце. GET — правило (404 — нет задачи или правила),
/// PUT — поставить/поменять (права — правка образца), DELETE — снять; копии остаются. Превью — ближайшие даты
/// сохранённого правила (GET) или черновика из тела (POST).
/// </summary>
[ApiController]
[Route("tasks/{id:guid}/recurrence")]
public class TaskRecurrenceController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskRecurrenceGetQuery(actor.Require(), id), cancellationToken) is { } rule ? Ok(rule) : NotFound();

    /// <summary>400 — интервал вне 1…99, день месяца, окончание раньше начала, «заранее» больше 60 дней.</summary>
    [HttpPut]
    public async Task<IActionResult> Set(Guid id, TaskRecurrenceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(new TaskRecurrenceSetCommand(actor.Require(), id, request), cancellationToken) is { } rule ? Ok(rule) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new TaskRecurrenceDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] int count = 5, CancellationToken cancellationToken = default) =>
        await mediator.Send(new TaskRecurrencePreviewQuery(actor.Require(), id, null, count), cancellationToken) is { } dates ? Ok(dates) : NotFound();

    [HttpPost("preview")]
    public async Task<IActionResult> PreviewDraft(Guid id, TaskRecurrenceRequest request, [FromQuery] int count = 5, CancellationToken cancellationToken = default)
    {
        try
        {
            return await mediator.Send(new TaskRecurrencePreviewQuery(actor.Require(), id, request, count), cancellationToken) is { } dates ? Ok(dates) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
