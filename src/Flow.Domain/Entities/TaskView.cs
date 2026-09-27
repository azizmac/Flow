namespace Flow.Domain.Entities;

/// <summary>
/// Представление экрана «Задачи» (docs/TZ_task_views.md): <see cref="UserPreferences.TasksView"/> — какое открывать
/// по умолчанию. Значения только дописываются: дерево, бэклог, роадмап и календарь появятся своими этапами.
/// </summary>
public enum TaskView
{
    List = 0,
    Board = 1
}
