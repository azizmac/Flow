using Flow.Shared.Contracts.Milestones;
using Flow.Shared.Contracts.Sprints;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Shared.Contracts.Dashboards;

/// <summary>Зеркало Domain.WidgetType.</summary>
public enum WidgetType
{
    TaskList = 0,
    Counter = 1,
    Breakdown = 2,
    CreatedVsClosed = 3,
    SprintBurndown = 4,
    MilestoneProgress = 5,
    Markdown = 6
}

/// <summary>
/// Настройки виджета — одна плоская запись на все виды, у каждого вида свои поля (docs/TZ_task_views.md §8):
/// список, счётчик, разбивка, создано/закрыто — Fql или FilterId (сохранённый фильтр); список — Limit;
/// разбивка — GroupBy (status, assignee, priority, type, project, cf:&lt;key&gt;) и Chart (bar, pie); создано/закрыто —
/// Days; burndown — BoardId (активный спринт проекта) или SprintId; веха — MilestoneId; заметка — Text.
/// </summary>
public sealed record WidgetConfig(
    string? Fql = null,
    Guid? FilterId = null,
    int? Limit = null,
    string? GroupBy = null,
    string? Chart = null,
    int? Days = null,
    Guid? BoardId = null,
    Guid? SprintId = null,
    Guid? MilestoneId = null,
    string? Text = null);

public sealed record DashboardWidgetResponse(Guid Id, WidgetType Type, string? Title, WidgetConfig Config, int X, int Y, int W, int H);

/// <summary>Дашборд; IsOwn — смотрящий его автор (может править), IsDefault — стартовый дашборд автора.</summary>
public sealed record DashboardResponse(
    Guid Id,
    string Name,
    bool Shared,
    bool IsDefault,
    Guid OwnerId,
    bool IsOwn,
    DateTime CreatedAt,
    IReadOnlyList<DashboardWidgetResponse> Widgets);

public sealed record CreateDashboardRequest(string Name, bool Shared = false);

/// <summary>PATCH /dashboards/{id}: null — не трогать.</summary>
public sealed record UpdateDashboardRequest(string? Name = null, bool? Shared = null, bool? IsDefault = null);

/// <summary>POST /dashboards/{id}/widgets: без X/Y — в первую свободную строку.</summary>
public sealed record AddWidgetRequest(WidgetType Type, string? Title, WidgetConfig Config, int? X = null, int? Y = null, int W = 6, int H = 3);

/// <summary>PATCH /dashboards/{id}/widgets/{widgetId}: виджет целиком — заголовок, настройки, место (сетка 12 колонок).</summary>
public sealed record UpdateWidgetRequest(string? Title, WidgetConfig Config, int X, int Y, int W, int H);

/// <summary>Группа разбивки: Key — Id или значение (null — «не задано»), Label — подпись.</summary>
public sealed record WidgetGroup(string? Key, string Label, int Count);

public sealed record WidgetDayPoint(DateOnly Date, int Created, int Closed);

/// <summary>
/// Данные одного виджета — считаются правами смотрящего. Error — виджет не смог (битый FQL, фильтр удалён, нет
/// активного спринта): дашборд показывает ошибку в рамке виджета, остальные живут. Link — куда ведёт «открыть все».
/// </summary>
public sealed record WidgetDataResponse(
    WidgetType Type,
    string? Error = null,
    IReadOnlyList<TaskResponse>? Tasks = null,
    int? Count = null,
    IReadOnlyList<WidgetGroup>? Groups = null,
    IReadOnlyList<WidgetDayPoint>? Days = null,
    SprintReportResponse? Sprint = null,
    MilestoneResponse? Milestone = null,
    string? Text = null,
    string? Fql = null);
