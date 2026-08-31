namespace Flow.Domain.Entities;

/// <summary>
/// Готовый набор стандартных статусов, которыми Board.Create засеивает новую доску (см. DefaultStatuses).
/// Не ограничивает доску: после создания через Board.AddStatus можно добавлять свои статусы без Type (null).
/// </summary>
public enum StatusType
{
    NotStarted = 0,
    InProgress = 1,
    InReview = 2,
    Done = 3
}
