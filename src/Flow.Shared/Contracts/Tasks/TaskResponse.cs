using Flow.Shared.Ids;

namespace Flow.Shared.Contracts.Tasks;

public sealed record TaskResponse(
    TaskId Id,
    string Code,
    string Title,
    string? Description,
    StatusId StatusId,
    DateTime CreatedAt);
