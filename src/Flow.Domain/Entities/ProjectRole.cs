namespace Flow.Domain.Entities;

/// <summary>
/// Роль в проекте (docs/TZ_project_access.md). Порядок = сила, сравнивать через <c>&gt;=</c>. Хранится как int —
/// только дописывать. Лестница зеркалит нижнюю часть глобальной <see cref="UserRole"/>: Reader → Viewer,
/// Member → Member, Developer → Developer, Admin и Owner → Admin.
/// </summary>
public enum ProjectRole
{
    Viewer = 0,
    Member = 1,
    Developer = 2,
    Admin = 3
}

public static class ProjectRoleExtensions
{
    /// <summary>Роль в проекте, которую глобальная роль даёт без всякого участия.</summary>
    public static ProjectRole ToProjectRole(this UserRole role) => role switch
    {
        UserRole.Reader => ProjectRole.Viewer,
        UserRole.Member => ProjectRole.Member,
        UserRole.Developer => ProjectRole.Developer,
        UserRole.Admin or UserRole.Owner => ProjectRole.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown user role.")
    };
}
