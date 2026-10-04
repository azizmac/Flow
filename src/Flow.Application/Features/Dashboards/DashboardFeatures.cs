using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Fql;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Dashboards;
using MediatR;
using DomainWidgetType = Flow.Domain.Entities.WidgetType;
using SharedWidgetType = Flow.Shared.Contracts.Dashboards.WidgetType;

namespace Flow.Application.Features.Dashboards;

/// <summary>Свои и общие дашборды, по имени (без виджетов).</summary>
public sealed record DashboardListQuery(Guid ActorId) : IRequest<IReadOnlyList<DashboardResponse>>;

/// <summary>Дашборд с виджетами; чужой личный или несуществующий — null (404).</summary>
public sealed record DashboardGetQuery(Guid ActorId, Guid DashboardId) : IRequest<DashboardResponse?>;

/// <summary>Стартовый дашборд смотрящего: свой «по умолчанию», иначе первый свой; нет своих — null.</summary>
public sealed record DashboardDefaultQuery(Guid ActorId) : IRequest<DashboardResponse?>;

/// <summary>
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record DashboardCreateCommand(Guid ActorId, string Name, bool Shared) : IRequest<DashboardResponse>;

/// <summary>
/// PATCH: null — не трогать; IsDefault = true снимает флаг с остальных дашбордов автора. Менять — автор.
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record DashboardUpdateCommand(Guid ActorId, Guid DashboardId, string? Name = null, bool? Shared = null, bool? IsDefault = null)
    : IRequest<DashboardResponse?>;

/// <summary>
/// Удалить — автор, общий — ещё Admin+. false — не найден.
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record DashboardDeleteCommand(Guid ActorId, Guid DashboardId) : IRequest<bool>;

/// <summary>
/// Новый виджет; настройки проверяются по виду (FQL биндится правами автора, фильтр — видимый ему). Менять — автор.
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record WidgetAddCommand(Guid ActorId, Guid DashboardId, DomainWidgetType Type, string? Title, WidgetConfig Config,
    int? X = null, int? Y = null, int W = 6, int H = 3) : IRequest<DashboardResponse?>;

/// <summary>
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record WidgetUpdateCommand(Guid ActorId, Guid DashboardId, Guid WidgetId, string? Title, WidgetConfig Config, int X, int Y, int W, int H)
    : IRequest<DashboardResponse?>;

/// <summary>
/// Обработчик - <see cref="DashboardHandlers"/>
/// </summary>
public sealed record WidgetRemoveCommand(Guid ActorId, Guid DashboardId, Guid WidgetId) : IRequest<DashboardResponse?>;

/// <summary>Данные виджета правами смотрящего; дашборд не виден — null (404), ошибка виджета — в ответе, не исключением.</summary>
public sealed record WidgetDataQuery(Guid ActorId, Guid DashboardId, Guid WidgetId) : IRequest<WidgetDataResponse?>;

/// <summary>Пределы виджетов — публичны ради тестов и клиента.</summary>
public static class DashboardLimits
{
    /// <summary>Строк в виджете-списке.</summary>
    public const int MaxLimit = 50;

    /// <summary>Длина заметки.</summary>
    public const int MaxTextLength = 10000;

    /// <summary>Групп разбивки; остальные сворачиваются в «Другие» — категориальная палитра держит восемь цветов.</summary>
    public const int MaxGroups = 7;
}

internal static class DashboardMapping
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    public static WidgetConfig ConfigOf(DashboardWidget widget) =>
        JsonSerializer.Deserialize<WidgetConfig>(widget.Config, Json) ?? new WidgetConfig();

    public static DashboardResponse ToResponse(this Dashboard dashboard, Guid actorId, bool withWidgets = true) => new(
        dashboard.Id,
        dashboard.Name,
        dashboard.Visibility == SavedFilterVisibility.Shared,
        dashboard.IsDefault,
        dashboard.OwnerId,
        dashboard.OwnerId == actorId,
        dashboard.CreatedAt,
        withWidgets
            ? dashboard.Widgets.OrderBy(w => w.Y).ThenBy(w => w.X)
                .Select(w => new DashboardWidgetResponse(w.Id, (SharedWidgetType)(int)w.Type, w.Title, ConfigOf(w), w.X, w.Y, w.W, w.H))
                .ToList()
            : []);
}

internal sealed class DashboardHandlers(
    IDashboardRepository dashboards,
    ISavedFilterRepository filters,
    IBoardRepository boards,
    IUserRepository users,
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ISprintRepository sprints,
    IMilestoneRepository milestones, IGroupRepository groupDirectory,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<DashboardListQuery, IReadOnlyList<DashboardResponse>>,
    IRequestHandler<DashboardGetQuery, DashboardResponse?>,
    IRequestHandler<DashboardDefaultQuery, DashboardResponse?>,
    IRequestHandler<DashboardCreateCommand, DashboardResponse>,
    IRequestHandler<DashboardUpdateCommand, DashboardResponse?>,
    IRequestHandler<DashboardDeleteCommand, bool>,
    IRequestHandler<WidgetAddCommand, DashboardResponse?>,
    IRequestHandler<WidgetUpdateCommand, DashboardResponse?>,
    IRequestHandler<WidgetRemoveCommand, DashboardResponse?>
{
    public static readonly IReadOnlySet<string> GroupFields = new HashSet<string> { "status", "assignee", "priority", "type", "project" };

    public async Task<IReadOnlyList<DashboardResponse>> Handle(DashboardListQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        return (await dashboards.GetVisibleAsync(actor.Id, cancellationToken)).Select(d => d.ToResponse(actor.Id, withWidgets: false)).ToList();
    }

    public async Task<DashboardResponse?> Handle(DashboardGetQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        return (await VisibleAsync(actor, request.DashboardId, cancellationToken))?.ToResponse(actor.Id);
    }

    public async Task<DashboardResponse?> Handle(DashboardDefaultQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var own = await dashboards.GetOwnedAsync(actor.Id, cancellationToken);
        var pick = own.FirstOrDefault(d => d.IsDefault) ?? own.OrderBy(d => d.CreatedAt).FirstOrDefault();
        return pick is null ? null : (await dashboards.GetByIdAsync(pick.Id, cancellationToken))!.ToResponse(actor.Id);
    }

    public async Task<DashboardResponse> Handle(DashboardCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var dashboard = Dashboard.Create(actor.Id, request.Name, request.Shared ? SavedFilterVisibility.Shared : SavedFilterVisibility.Private);
        // Первый свой дашборд сразу становится стартовым — выбирать его отдельно незачем.
        if ((await dashboards.GetOwnedAsync(actor.Id, cancellationToken)).Count == 0)
            dashboard.SetDefault(true);
        dashboards.Add(dashboard);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dashboard.ToResponse(actor.Id);
    }

    public async Task<DashboardResponse?> Handle(DashboardUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var dashboard = await VisibleAsync(actor, request.DashboardId, cancellationToken);
        if (dashboard is null)
            return null;

        permissions.EnsureCanEditDashboard(actor, dashboard);
        if (request.Name is not null)
            dashboard.Rename(request.Name);
        if (request.Shared is { } shared)
            dashboard.SetVisibility(shared ? SavedFilterVisibility.Shared : SavedFilterVisibility.Private);
        if (request.IsDefault is { } isDefault)
        {
            if (isDefault)
                foreach (var other in (await dashboards.GetOwnedAsync(actor.Id, cancellationToken)).Where(d => d.Id != dashboard.Id))
                    other.SetDefault(false);
            dashboard.SetDefault(isDefault);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dashboard.ToResponse(actor.Id);
    }

    public async Task<bool> Handle(DashboardDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var dashboard = await VisibleAsync(actor, request.DashboardId, cancellationToken);
        if (dashboard is null)
            return false;

        permissions.EnsureCanDeleteDashboard(actor, dashboard);
        dashboards.Remove(dashboard);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<DashboardResponse?> Handle(WidgetAddCommand request, CancellationToken cancellationToken) =>
        EditAsync(request.ActorId, request.DashboardId, async (actor, dashboard) =>
        {
            var config = await ValidateAsync(actor, request.Type, request.Config, cancellationToken);
            dashboard.AddWidget(request.Type, request.Title, config, request.X, request.Y, request.W, request.H);
        }, cancellationToken);

    public Task<DashboardResponse?> Handle(WidgetUpdateCommand request, CancellationToken cancellationToken) =>
        EditAsync(request.ActorId, request.DashboardId, async (actor, dashboard) =>
        {
            var widget = dashboard.GetWidget(request.WidgetId);
            var config = await ValidateAsync(actor, widget.Type, request.Config, cancellationToken);
            dashboard.UpdateWidget(widget.Id, request.Title, config, request.X, request.Y, request.W, request.H);
        }, cancellationToken);

    public Task<DashboardResponse?> Handle(WidgetRemoveCommand request, CancellationToken cancellationToken) =>
        EditAsync(request.ActorId, request.DashboardId, (_, dashboard) =>
        {
            dashboard.RemoveWidget(request.WidgetId);
            return Task.CompletedTask;
        }, cancellationToken);

    private async Task<DashboardResponse?> EditAsync(Guid actorId, Guid dashboardId, Func<User, Dashboard, Task> change, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        var dashboard = await VisibleAsync(actor, dashboardId, cancellationToken);
        if (dashboard is null)
            return null;

        permissions.EnsureCanEditDashboard(actor, dashboard);
        await change(actor, dashboard);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return dashboard.ToResponse(actor.Id);
    }

    private async Task<Dashboard?> VisibleAsync(User actor, Guid id, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetByIdAsync(id, cancellationToken);
        return dashboard is not null && dashboard.IsVisibleTo(actor.Id) ? dashboard : null;
    }

    /// <summary>
    /// Настройки по виду: у задачных виджетов — FQL (проверяется биндингом правами автора, как сохранённый фильтр) или
    /// видимый автору сохранённый фильтр; у разбивки — поле группировки; у burndown — проект или спринт; у вехи — веха.
    /// Лишние поля отбрасываются, чтобы в jsonb лежало только то, что вид читает.
    /// </summary>
    private async Task<string> ValidateAsync(User actor, DomainWidgetType type, WidgetConfig config, CancellationToken cancellationToken)
    {
        async Task<WidgetConfig> Query(WidgetConfig c)
        {
            if (c.FilterId is { } filterId)
            {
                var filter = await filters.GetByIdAsync(filterId, cancellationToken);
                if (filter is null || !filter.IsVisibleTo(actor.Id))
                    throw new InvalidOperationException("Сохранённый фильтр не найден.");
                return c with { Fql = null };
            }

            var fql = string.IsNullOrWhiteSpace(c.Fql) ? null : c.Fql.Trim();
            if (fql is not null)
            {
                var lookup = new FqlLookup(actor, boards, users, tasks, links, projectAccess, sprints, milestones, groupDirectory);
                await FqlBinder.BindAsync(fql, lookup, actor.Id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
            }
            return c with { Fql = fql };
        }

        WidgetConfig clean = type switch
        {
            DomainWidgetType.TaskList => await Query(new WidgetConfig(config.Fql, config.FilterId, Math.Clamp(config.Limit ?? 10, 1, DashboardLimits.MaxLimit))),
            DomainWidgetType.Counter => await Query(new WidgetConfig(config.Fql, config.FilterId)),
            DomainWidgetType.Breakdown => await Query(new WidgetConfig(config.Fql, config.FilterId,
                GroupBy: GroupFields.Contains(config.GroupBy ?? "") || (config.GroupBy?.StartsWith("cf:", StringComparison.Ordinal) ?? false)
                    ? config.GroupBy
                    : throw new ArgumentException("Разбивка — по status, assignee, priority, type, project или cf:<ключ>."),
                Chart: config.Chart == "pie" ? "pie" : "bar")),
            DomainWidgetType.CreatedVsClosed => await Query(new WidgetConfig(config.Fql, config.FilterId, Days: Math.Clamp(config.Days ?? 30, 7, 90))),
            DomainWidgetType.SprintBurndown => config.SprintId is not null || config.BoardId is not null
                ? new WidgetConfig(BoardId: config.SprintId is null ? config.BoardId : null, SprintId: config.SprintId)
                : throw new ArgumentException("Для burndown укажите проект (его активный спринт) или спринт."),
            DomainWidgetType.MilestoneProgress => config.MilestoneId is { } m
                ? new WidgetConfig(MilestoneId: m)
                : throw new ArgumentException("Укажите веху."),
            _ => (config.Text ?? "").Length <= DashboardLimits.MaxTextLength
                ? new WidgetConfig(Text: config.Text ?? "")
                : throw new ArgumentException($"Заметка — не длиннее {DashboardLimits.MaxTextLength} символов.")
        };

        return JsonSerializer.Serialize(clean, DashboardMapping.Json);
    }
}
