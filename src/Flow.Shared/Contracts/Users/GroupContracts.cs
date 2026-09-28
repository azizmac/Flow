namespace Flow.Shared.Contracts.Users;

/// <summary>Группа людей (docs/TZ_project_access.md §1, этап 4C) — ей выдают роль в проекте.</summary>
/// <summary>IsTeam (этап 4D) — группа-команда: её указывают в задаче, по ней фильтруют и раскладывают дорожки канбана.</summary>
public sealed record GroupResponse(Guid Id, string Name, string? Description, IReadOnlyList<Guid> MemberIds, DateTime CreatedAt, bool IsTeam = false);

public sealed record SaveGroupRequest(string Name, string? Description = null, bool IsTeam = false);
