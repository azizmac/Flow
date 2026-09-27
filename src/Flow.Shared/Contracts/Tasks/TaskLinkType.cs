namespace Flow.Shared.Contracts.Tasks;

/// <summary>Зеркало Flow.Domain.Entities.TaskLinkType (Shared не ссылается на Domain): значения совпадают.</summary>
public enum TaskLinkType
{
    Blocks = 0,
    Duplicates = 1,
    RelatesTo = 2,
    Clones = 3,
    SplitFrom = 4
}
