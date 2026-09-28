namespace Flow.Domain.Entities;

/// <summary>Вид виджета дашборда (docs/TZ_task_views.md §8); числа хранятся в БД — только дописывать.</summary>
public enum WidgetType
{
    /// <summary>Список задач по FQL или сохранённому фильтру.</summary>
    TaskList = 0,

    /// <summary>Число задач по FQL.</summary>
    Counter = 1,

    /// <summary>Разбивка задач по полю (статус, исполнитель, приоритет, тип, проект, cf.*) — столбцы или круг.</summary>
    Breakdown = 2,

    /// <summary>Создано и закрыто по дням за период.</summary>
    CreatedVsClosed = 3,

    /// <summary>Burndown активного спринта проекта или конкретного спринта.</summary>
    SprintBurndown = 4,

    /// <summary>Прогресс вехи.</summary>
    MilestoneProgress = 5,

    /// <summary>Заметка в Markdown.</summary>
    Markdown = 6
}

/// <summary>
/// Виджет дашборда: вид, заголовок, настройки (JSON — у каждого вида свои, проверяет Application) и место в сетке
/// из 12 колонок (X — колонка 0…11, W — ширина 1…12, Y — строка, H — высота в строках 1…8).
/// </summary>
public sealed class DashboardWidget
{
    public const int Columns = 12;
    public const int MaxHeight = 8;
    public const int TitleMaxLength = 80;
    public const int ConfigMaxLength = 20000;

    public Guid Id { get; private set; }

    public Guid DashboardId { get; private set; }

    public WidgetType Type { get; private set; }

    public string? Title { get; private set; }

    public string Config { get; private set; } = "{}";

    public int X { get; private set; }

    public int Y { get; private set; }

    public int W { get; private set; }

    public int H { get; private set; }

    private DashboardWidget()
    {
        // EF Core
    }

    internal DashboardWidget(Guid dashboardId, WidgetType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentException($"Unknown widget type {type}.", nameof(type));

        Id = Guid.NewGuid();
        DashboardId = dashboardId;
        Type = type;
    }

    internal void Update(string? title, string config, int x, int y, int w, int h)
    {
        var trimmed = title?.Trim();
        if (trimmed is { Length: > TitleMaxLength })
            throw new ArgumentException($"Widget title must be at most {TitleMaxLength} characters.", nameof(title));
        if (string.IsNullOrWhiteSpace(config) || config.Length > ConfigMaxLength || !config.TrimStart().StartsWith('{'))
            throw new ArgumentException($"Widget config must be a JSON object up to {ConfigMaxLength} characters.", nameof(config));
        if (w is < 1 or > Columns || x < 0 || x + w > Columns)
            throw new ArgumentException($"Виджет должен помещаться в {Columns} колонок: X от 0, ширина от 1, X + ширина ≤ {Columns}.", nameof(w));
        if (y < 0 || h is < 1 or > MaxHeight)
            throw new ArgumentException($"Строка — от 0, высота — от 1 до {MaxHeight}.", nameof(h));

        Title = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        Config = config;
        X = x;
        Y = y;
        W = w;
        H = h;
    }
}

/// <summary>
/// Дашборд (docs/TZ_task_views.md §8): набор виджетов владельца. Общий видят все, но данные каждого виджета считаются
/// правами смотрящего — дашборд хранит запросы, а не задачи (как сохранённый фильтр). IsDefault — дашборд, который
/// владелец видит на стартовой странице «Дашборд»; у владельца такой один.
/// </summary>
public sealed class Dashboard
{
    public const int NameMaxLength = 80;
    public const int MaxWidgets = 30;

    private readonly List<DashboardWidget> _widgets = [];

    public Guid Id { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public SavedFilterVisibility Visibility { get; private set; }

    public bool IsDefault { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public IReadOnlyCollection<DashboardWidget> Widgets => _widgets;

    private Dashboard()
    {
        // EF Core
    }

    public static Dashboard Create(Guid ownerId, string name, SavedFilterVisibility visibility)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner id must not be empty.", nameof(ownerId));

        var dashboard = new Dashboard { Id = Guid.NewGuid(), OwnerId = ownerId, CreatedAt = DateTime.UtcNow };
        dashboard.Rename(name);
        dashboard.SetVisibility(visibility);
        return dashboard;
    }

    public bool IsVisibleTo(Guid userId) => OwnerId == userId || Visibility == SavedFilterVisibility.Shared;

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new ArgumentException("Dashboard name must not be empty.", nameof(name));
        if (trimmed.Length > NameMaxLength)
            throw new ArgumentException($"Dashboard name must be at most {NameMaxLength} characters.", nameof(name));
        Name = trimmed;
    }

    public void SetVisibility(SavedFilterVisibility visibility) =>
        Visibility = Enum.IsDefined(visibility) ? visibility : throw new ArgumentException($"Unknown visibility {visibility}.", nameof(visibility));

    /// <summary>Флаг «по умолчанию»; что у владельца он один, следит хендлер — остальные дашборды владельца он снимает сам.</summary>
    public void SetDefault(bool isDefault) => IsDefault = isDefault;

    /// <summary>Новый виджет; без места — в первое свободное место сетки сверху вниз, слева направо.</summary>
    public DashboardWidget AddWidget(WidgetType type, string? title, string config, int? x = null, int? y = null, int w = 6, int h = 3)
    {
        if (_widgets.Count >= MaxWidgets)
            throw new InvalidOperationException($"На дашборде не больше {MaxWidgets} виджетов.");

        var widget = new DashboardWidget(Id, type);
        var (freeX, freeY) = x is null && y is null ? FreeSlot(w, h) : (x ?? 0, y ?? 0);
        widget.Update(title, config, freeX, freeY, w, h);
        _widgets.Add(widget);
        return widget;
    }

    /// <summary>
    /// Первое место под прямоугольник w×h, которое не пересекает ни один виджет. Под последним виджетом место есть
    /// всегда, поэтому перебор конечен; ширину вне сетки отклонит Update.
    /// </summary>
    private (int X, int Y) FreeSlot(int w, int h)
    {
        if (w is < 1 or > DashboardWidget.Columns)
            return (0, 0);

        var bottom = _widgets.Count == 0 ? 0 : _widgets.Max(v => v.Y + v.H);
        for (var y = 0; y <= bottom; y++)
            for (var x = 0; x + w <= DashboardWidget.Columns; x++)
                if (!_widgets.Any(v => x < v.X + v.W && v.X < x + w && y < v.Y + v.H && v.Y < y + h))
                    return (x, y);
        return (0, bottom);
    }

    public DashboardWidget UpdateWidget(Guid widgetId, string? title, string config, int x, int y, int w, int h)
    {
        var widget = GetWidget(widgetId);
        widget.Update(title, config, x, y, w, h);
        return widget;
    }

    public void RemoveWidget(Guid widgetId) => _widgets.Remove(GetWidget(widgetId));

    public DashboardWidget GetWidget(Guid widgetId) =>
        _widgets.FirstOrDefault(w => w.Id == widgetId)
        ?? throw new InvalidOperationException($"Widget {widgetId} does not belong to dashboard {Id}.");
}
