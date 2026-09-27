namespace Flow.Shared.Contracts.Boards;

/// <summary>PUT /boards/{id}/default-role: Viewer, Member или Developer; null — снять ограничение.</summary>
public sealed record SetDefaultRoleRequest(ProjectRole? Role);
