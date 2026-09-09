namespace Flow.Shared.Contracts.Tasks;

/// <summary>BoardId нужен странице /tasks/{id}: по нему клиент подгружает ключ и статусы проекта.</summary>
public sealed record TaskResponse(
    Guid Id,
    Guid BoardId,
    string Code,
    string Title,
    string? Description,
    Guid StatusId,
    Guid? AssigneeId,
    DateTime CreatedAt);
