namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// TaskCount и NextTaskNumber нужны карточке проекта в клиенте («12 задач», «следующая FRONT-13»),
/// чтобы не делать N+1 запросов GET /boards/{id}/tasks ради счётчика.
/// </summary>
public sealed record BoardResponse(
    Guid Id,
    string Key,
    string Name,
    DateTime CreatedAt,
    int TaskCount,
    int NextTaskNumber,
    IReadOnlyList<StatusResponse> Statuses);
