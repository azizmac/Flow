using Flow.Application.Abstractions;
using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Fql;
using Flow.Shared.Contracts.Filters;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flow.Api.Controllers;

/// <summary>
/// Сохранённые фильтры (docs/TZ_task_views.md §7). Чужой личный фильтр — 404; править — автор (иначе 403),
/// удалять — автор или Admin+ для общего. Битый FQL — 400 с позицией, как у GET /tasks?fql=.
/// </summary>
[ApiController]
[Route("filters")]
public class FiltersController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new SavedFilterListQuery(actor.Require()), cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new SavedFilterGetQuery(actor.Require(), id), cancellationToken) is { } filter ? Ok(filter) : NotFound();

    [HttpPost]
    public Task<IActionResult> Create(CreateSavedFilterRequest request, CancellationToken cancellationToken) =>
        Send(async () =>
        {
            var created = await mediator.Send(new SavedFilterCreateCommand(actor.Require(), request.Name, request.Query, request.Shared), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        });

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateSavedFilterRequest request, CancellationToken cancellationToken) =>
        Send(async () =>
            await mediator.Send(new SavedFilterUpdateCommand(actor.Require(), id, request.Name, request.Query, request.Shared), cancellationToken) is { } filter
                ? Ok(filter)
                : NotFound());

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new SavedFilterDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    [HttpPut("{id:guid}/star")]
    public Task<IActionResult> Star(Guid id, CancellationToken cancellationToken) => SetStar(id, true, cancellationToken);

    [HttpDelete("{id:guid}/star")]
    public Task<IActionResult> Unstar(Guid id, CancellationToken cancellationToken) => SetStar(id, false, cancellationToken);

    private async Task<IActionResult> SetStar(Guid id, bool starred, CancellationToken cancellationToken) =>
        await mediator.Send(new SavedFilterStarCommand(actor.Require(), id, starred), cancellationToken) is { } filter ? Ok(filter) : NotFound();

    private async Task<IActionResult> Send(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (FqlException ex)
        {
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
