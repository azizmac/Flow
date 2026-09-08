using Flow.Shared.Ids;

namespace Flow.Shared.Contracts.Boards;

public sealed record BoardResponse(
    BoardId Id,
    string Key,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<StatusResponse> Statuses);
