namespace Flow.Domain.Entities;

/// <summary>
/// Приоритет задачи — фиксированная шкала, а не справочник проекта: её можно сортировать и фильтровать во всех
/// проектах сразу без join (docs/TZ_task_model.md §2). Порядок значений = важность, сравнивать через <c>&gt;=</c>.
/// Хранится как int — только дописывать.
/// </summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
