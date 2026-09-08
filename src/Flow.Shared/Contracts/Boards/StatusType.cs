namespace Flow.Shared.Contracts.Boards;

/// <summary>Зеркало Flow.Domain.Entities.StatusType — Flow.Shared намеренно не ссылается на Flow.Domain.</summary>
public enum StatusType
{
    NotStarted = 0,
    InProgress = 1,
    InReview = 2,
    Done = 3
}
