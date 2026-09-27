using Flow.Application.Abstractions;
using Flow.Application.Features.Dashboards;
using Flow.Application.Features.Tasks.Fql;
using Flow.Shared.Contracts.Filters;
using Flow.Shared.Contracts.Dashboards;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;

namespace Flow.Api.Controllers;

/// <summary>
/// Дашборды (docs/TZ_task_views.md §8): личные и общие, виджеты сеткой 12 колонок. Данные виджета — отдельным
/// запросом: каждый виджет грузится и падает сам по себе, а считается правами смотрящего.
/// </summary>
[ApiController]
[Route("dashboards")]
public class DashboardsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok(await mediator.Send(new DashboardListQuery(actor.Require()), cancellationToken));

    /// <summary>Дашборд по умолчанию текущего пользователя; 204 — его нет.</summary>
    [HttpGet("default")]
    public async Task<IActionResult> Default(CancellationToken cancellationToken) =>
        await mediator.Send(new DashboardDefaultQuery(actor.Require()), cancellationToken) is { } dashboard ? Ok(dashboard) : NoContent();

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new DashboardGetQuery(actor.Require(), id), cancellationToken) is { } dashboard ? Ok(dashboard) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create(CreateDashboardRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var dashboard = await mediator.Send(new DashboardCreateCommand(actor.Require(), request.Name, request.Shared), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = dashboard.Id }, dashboard);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, UpdateDashboardRequest request, CancellationToken cancellationToken) =>
        Send(new DashboardUpdateCommand(actor.Require(), id, request.Name, request.Shared, request.IsDefault), cancellationToken);

    /// <summary>Автор; общий — ещё Admin+.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await mediator.Send(new DashboardDeleteCommand(actor.Require(), id), cancellationToken) ? NoContent() : NotFound();

    /// <summary>400 — битый FQL ({ message, position, length }), недоступный фильтр, позиция вне сетки, больше 30 виджетов.</summary>
    [HttpPost("{id:guid}/widgets")]
    public Task<IActionResult> AddWidget(Guid id, AddWidgetRequest request, CancellationToken cancellationToken) =>
        Send(new WidgetAddCommand(actor.Require(), id, (DomainWidgetType)(int)request.Type, request.Title, request.Config,
            request.X, request.Y, request.W, request.H), cancellationToken);

    [HttpPatch("{id:guid}/widgets/{widgetId:guid}")]
    public Task<IActionResult> UpdateWidget(Guid id, Guid widgetId, UpdateWidgetRequest request, CancellationToken cancellationToken) =>
        Send(new WidgetUpdateCommand(actor.Require(), id, widgetId, request.Title, request.Config, request.X, request.Y, request.W, request.H),
            cancellationToken);

    [HttpDelete("{id:guid}/widgets/{widgetId:guid}")]
    public Task<IActionResult> RemoveWidget(Guid id, Guid widgetId, CancellationToken cancellationToken) =>
        Send(new WidgetRemoveCommand(actor.Require(), id, widgetId), cancellationToken);

    /// <summary>Ошибка самого виджета (FQL, удалённый фильтр, нет спринта) — 200 с полем error.</summary>
    [HttpGet("{id:guid}/widgets/{widgetId:guid}/data")]
    public async Task<IActionResult> WidgetData(Guid id, Guid widgetId, CancellationToken cancellationToken) =>
        await mediator.Send(new WidgetDataQuery(actor.Require(), id, widgetId), cancellationToken) is { } data ? Ok(data) : NotFound();

    private async Task<IActionResult> Send(IRequest<DashboardResponse?> command, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(command, cancellationToken) is { } dashboard ? Ok(dashboard) : NotFound();
        }
        catch (FqlException ex)
        {
            return BadRequest(new FqlErrorResponse(ex.Message, ex.Position, ex.Length));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
