namespace Flow.Shared.Contracts.Boards;

public sealed record BoardResponse(
    Guid Id,
    string Key,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<StatusResponse> Statuses);
