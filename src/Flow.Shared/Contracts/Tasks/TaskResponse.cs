namespace Flow.Shared.Contracts.Tasks;

public sealed record TaskResponse(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid StatusId,
    DateTime CreatedAt);
