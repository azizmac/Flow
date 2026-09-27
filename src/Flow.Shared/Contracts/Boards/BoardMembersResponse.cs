namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// Доступ к проекту: роль по умолчанию (null — роль в проекте равна роли во Flow) и прямые участники.
/// Участие только повышает роль; глобальные Admin и Owner — администраторы любого проекта и без участия.
/// </summary>
public sealed record BoardMembersResponse(Guid BoardId, ProjectRole? DefaultRole, IReadOnlyList<BoardMemberResponse> Members);

public sealed record BoardMemberResponse(Guid UserId, ProjectRole Role, Guid AddedById, DateTime AddedAt);
