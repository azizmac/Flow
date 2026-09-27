namespace Flow.Shared.Contracts.Users;

/// <summary>Зеркало Domain.TaskView: представление экрана «Задачи» по умолчанию (docs/TZ_task_views.md).</summary>
public enum TaskView
{
    List = 0,
    Board = 1,

    /// <summary>Дерево — только внутри проекта; в «Все проекты» экран покажет список.</summary>
    Tree = 2,

    /// <summary>Бэклог и спринты — тоже только внутри проекта.</summary>
    Backlog = 3
}
