namespace Flow.Shared.Contracts.Users;

/// <summary>Группа людей (docs/TZ_project_access.md §1, этап 4C) — ей выдают роль в проекте.</summary>
public sealed record GroupResponse(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, DateTime CreatedAt);

public sealed record SaveGroupRequest(string Name, string? Description = null);
