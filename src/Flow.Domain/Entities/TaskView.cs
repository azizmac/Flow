namespace Flow.Domain.Entities;

/// <summary>
/// Представление экрана «Задачи» (docs/TZ_task_views.md): <see cref="UserPreferences.TasksView"/> — какое открывать
/// по умолчанию. Значения только дописываются: дерево, бэклог, роадмап и календарь появятся своими этапами.
/// </summary>
public enum TaskView
{
    List = 0,
    Board = 1,

    /// <summary>Дерево — только внутри проекта; в «Все проекты» экран покажет список.</summary>
    Tree = 2,

    /// <summary>Бэклог и спринты — тоже только внутри проекта.</summary>
    Backlog = 3,

    /// <summary>Календарь месяца или недели (docs/TZ_task_views.md §5).</summary>
    Calendar = 4,

    /// <summary>Роадмап проекта — полосы от начала до срока (docs/TZ_task_views.md §4).</summary>
    Roadmap = 5
}
