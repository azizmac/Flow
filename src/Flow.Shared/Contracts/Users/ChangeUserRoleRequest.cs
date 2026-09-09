namespace Flow.Shared.Contracts.Users;

/// <summary>PATCH /users/{id}/role. 403 — роль actor'а не позволяет; 400 — последний Owner.</summary>
public sealed record ChangeUserRoleRequest(UserRole Role);
