namespace Flow.Domain.Entities;

/// <summary>Куда вести с корня сайта (<see cref="UserPreferences.StartPage"/>).</summary>
public enum StartPage
{
    Projects = 0,
    Tasks = 1,

    /// <summary>Экран «Задачи» с фильтром по себе как исполнителю.</summary>
    MyTasks = 2,

    /// <summary>Дашборд по умолчанию (docs/TZ_task_views.md §8); нет такого — список дашбордов.</summary>
    Dashboard = 3
}
