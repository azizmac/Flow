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

    public static UserPreferences Default { get; } = new(SidebarMode.Auto, StartPage.Projects, DefaultTasksPageSize);

    public SidebarMode SidebarMode { get; private set; }

    public StartPage StartPage { get; private set; }

    /// <summary>Строк на странице списка задач при открытии экрана.</summary>
    public int TasksPageSize { get; private set; }

    private UserPreferences()
    {
        // EF Core
    }

    private UserPreferences(SidebarMode sidebarMode, StartPage startPage, int tasksPageSize)
    {
        SidebarMode = sidebarMode;
        StartPage = startPage;
        TasksPageSize = tasksPageSize;
    }

    public static UserPreferences Create(SidebarMode sidebarMode, StartPage startPage, int tasksPageSize)
    {
        if (!Enum.IsDefined(sidebarMode))
            throw new ArgumentException($"Unknown sidebar mode {sidebarMode}.", nameof(sidebarMode));

        if (!Enum.IsDefined(startPage))
            throw new ArgumentException($"Unknown start page {startPage}.", nameof(startPage));

        if (!AllowedTasksPageSizes.Contains(tasksPageSize))
            throw new ArgumentException(
                $"Tasks page size must be one of {string.Join(", ", AllowedTasksPageSizes)}.",
                nameof(tasksPageSize));

        return new UserPreferences(sidebarMode, startPage, tasksPageSize);
    }

    /// <summary>Копия с заменой заданных полей; null — оставить как есть (PATCH-семантика команды).</summary>
    public UserPreferences With(SidebarMode? sidebarMode = null, StartPage? startPage = null, int? tasksPageSize = null)
        => Create(sidebarMode ?? SidebarMode, startPage ?? StartPage, tasksPageSize ?? TasksPageSize);

    public bool SameAs(UserPreferences other)
        => SidebarMode == other.SidebarMode && StartPage == other.StartPage && TasksPageSize == other.TasksPageSize;
}
