namespace Flow.Shared.Contracts.Tasks;

/// <summary>Зеркало Flow.Domain.Entities.TaskPriority. Порядок = важность, значения совпадают.</summary>
public enum TaskPriority
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}
