namespace Flow.Shared.Contracts.Tasks;

/// <summary>AssigneeId — только id исполнителя; карточка пользователя запрашивается отдельно (GET /users/{id}).</summary>
public sealed record TaskResponse(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid StatusId,
    Guid? AssigneeId,
    DateTime CreatedAt);
