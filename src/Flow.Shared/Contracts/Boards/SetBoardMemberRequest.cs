namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// PUT /boards/{id}/members/{userId} и /boards/{id}/groups/{groupId}: роль участия. PermissionSetId (этап 4E) — свой
/// набор прав: тогда роль — его базовая роль (присланная Role не важна).
/// </summary>
public sealed record SetBoardMemberRequest(ProjectRole Role, Guid? PermissionSetId = null);

/// <summary>Набор прав (этап 4E): встроенный — роль проекта (не правится), свой — клон с базовой ролью.</summary>
public sealed record PermissionSetResponse(Guid Id, string Name, string? Description, ProjectRole BaseRole, IReadOnlyList<ProjectPermission> Permissions, bool IsBuiltIn);

public sealed record SavePermissionSetRequest(string Name, string? Description, ProjectRole BaseRole, IReadOnlyList<ProjectPermission> Permissions);
