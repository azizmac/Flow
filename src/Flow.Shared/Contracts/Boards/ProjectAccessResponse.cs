namespace Flow.Shared.Contracts.Boards;

/// <summary>Роль текущего пользователя в проекте и её права (GET /boards/my-access, /boards/{id}/my-access).</summary>
public sealed record ProjectAccessResponse(Guid BoardId, ProjectRole Role, IReadOnlyList<ProjectPermission> Permissions);
