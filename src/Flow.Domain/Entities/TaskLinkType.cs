namespace Flow.Domain.Entities;

/// <summary>
/// Вид связи между задачами (docs/TZ_task_model.md §5). Хранится как int — порядок не менять, только дописывать.
/// Связь направленная: «A блокирует B»; <see cref="RelatesTo"/> симметрична и хранится одной строкой.
/// </summary>
public enum TaskLinkType
{
    Blocks = 0,
    Duplicates = 1,
    RelatesTo = 2,
    Clones = 3,
    SplitFrom = 4
}
