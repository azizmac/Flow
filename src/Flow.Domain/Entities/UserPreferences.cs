namespace Flow.Domain.Entities;

/// <summary>
/// Личные настройки интерфейса — value object внутри <see cref="User"/>. Живут на сервере, а не в браузере,
/// чтобы ехать за человеком на любой компьютер. Меняется целиком (<see cref="User.ChangePreferences"/>):
/// своей идентичности у настроек нет, поэтому в EF это complex type, колонки лежат в самой таблице Users.
/// </summary>
public sealed class UserPreferences
{
    /// <summary>Те же размеры, что предлагает пейджер списка задач: другой было бы нечем выбрать в интерфейсе.</summary>
    public static readonly IReadOnlyList<int> AllowedTasksPageSizes = [10, 25, 50, 100];

    public const int DefaultTasksPageSize = 100;

    public static UserPreferences Default { get; } = new(SidebarMode.Auto, StartPage.Projects, DefaultTasksPageSize, TaskView.List);

    public SidebarMode SidebarMode { get; private set; }

    public StartPage StartPage { get; private set; }

    /// <summary>Строк на странице списка задач при открытии экрана.</summary>
    public int TasksPageSize { get; private set; }

    /// <summary>Представление экрана «Задачи», когда в адресе нет ?view= (docs/TZ_task_views.md).</summary>
    public TaskView TasksView { get; private set; }

    private UserPreferences()
    {
        // EF Core
    }

    private UserPreferences(SidebarMode sidebarMode, StartPage startPage, int tasksPageSize, TaskView tasksView)
    {
        SidebarMode = sidebarMode;
        StartPage = startPage;
        TasksPageSize = tasksPageSize;
        TasksView = tasksView;
    }

    public static UserPreferences Create(SidebarMode sidebarMode, StartPage startPage, int tasksPageSize, TaskView tasksView = TaskView.List)
    {
        if (!Enum.IsDefined(tasksView))
            throw new ArgumentException($"Unknown tasks view {tasksView}.", nameof(tasksView));

        if (!Enum.IsDefined(sidebarMode))
            throw new ArgumentException($"Unknown sidebar mode {sidebarMode}.", nameof(sidebarMode));

        if (!Enum.IsDefined(startPage))
            throw new ArgumentException($"Unknown start page {startPage}.", nameof(startPage));

        if (!AllowedTasksPageSizes.Contains(tasksPageSize))
            throw new ArgumentException(
                $"Tasks page size must be one of {string.Join(", ", AllowedTasksPageSizes)}.",
                nameof(tasksPageSize));

        return new UserPreferences(sidebarMode, startPage, tasksPageSize, tasksView);
    }

    /// <summary>Копия с заменой заданных полей; null — оставить как есть (PATCH-семантика команды).</summary>
    public UserPreferences With(SidebarMode? sidebarMode = null, StartPage? startPage = null, int? tasksPageSize = null, TaskView? tasksView = null)
        => Create(sidebarMode ?? SidebarMode, startPage ?? StartPage, tasksPageSize ?? TasksPageSize, tasksView ?? TasksView);

    public bool SameAs(UserPreferences other)
        => SidebarMode == other.SidebarMode && StartPage == other.StartPage && TasksPageSize == other.TasksPageSize && TasksView == other.TasksView;
}
