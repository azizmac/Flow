using Flow.Application.Features.Dashboards;
using Flow.Client.Services;
using Flow.Shared.Contracts.Dashboards;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;

namespace Flow.Api.Client;

/// <summary>Дашборды (docs/TZ_task_views.md §8) — коды как у DashboardsController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<DashboardResponse>>> GetDashboards(CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new DashboardListQuery(await ActorAsync()), ct)));

    /// <summary>Нет дашборда по умолчанию — 404 (у контроллера 204).</summary>
    public Task<ApiResult<DashboardResponse>> GetDefaultDashboard(CancellationToken ct = default) =>
        SendDashboard(actor => new DashboardDefaultQuery(actor), ct);

    public Task<ApiResult<DashboardResponse>> GetDashboard(Guid dashboardId, CancellationToken ct = default) =>
        SendDashboard(actor => new DashboardGetQuery(actor, dashboardId), ct);

    public Task<ApiResult<DashboardResponse>> CreateDashboard(CreateDashboardRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => Ok(await mediator.Send(new DashboardCreateCommand(await ActorAsync(), request.Name, request.Shared), ct)));

    public Task<ApiResult<DashboardResponse>> UpdateDashboard(Guid dashboardId, UpdateDashboardRequest request, CancellationToken ct = default) =>
        SendDashboard(actor => new DashboardUpdateCommand(actor, dashboardId, request.Name, request.Shared, request.IsDefault), ct);

    public Task<ApiResult<bool>> DeleteDashboard(Guid dashboardId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new DashboardDeleteCommand(await ActorAsync(), dashboardId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<DashboardResponse>> AddWidget(Guid dashboardId, AddWidgetRequest request, CancellationToken ct = default) =>
        SendDashboard(actor => new WidgetAddCommand(actor, dashboardId, (DomainWidgetType)(int)request.Type, request.Title, request.Config,
            request.X, request.Y, request.W, request.H), ct);

    public Task<ApiResult<DashboardResponse>> UpdateWidget(Guid dashboardId, Guid widgetId, UpdateWidgetRequest request, CancellationToken ct = default) =>
        SendDashboard(actor => new WidgetUpdateCommand(actor, dashboardId, widgetId, request.Title, request.Config, request.X, request.Y, request.W, request.H), ct);

    public Task<ApiResult<DashboardResponse>> RemoveWidget(Guid dashboardId, Guid widgetId, CancellationToken ct = default) =>
        SendDashboard(actor => new WidgetRemoveCommand(actor, dashboardId, widgetId), ct);

    public Task<ApiResult<WidgetDataResponse>> GetWidgetData(Guid dashboardId, Guid widgetId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new WidgetDataQuery(await ActorAsync(), dashboardId, widgetId), ct) is { } data ? Ok(data) : NotFound<WidgetDataResponse>());

    private Task<ApiResult<DashboardResponse>> SendDashboard(Func<Guid, MediatR.IRequest<DashboardResponse?>> request, CancellationToken ct) =>
        Scoped(async mediator =>
            await mediator.Send(request(await ActorAsync()), ct) is { } dashboard ? Ok(dashboard) : NotFound<DashboardResponse>());
}
