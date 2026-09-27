using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>
/// Роль и права в проекте (docs/TZ_project_access.md §1–2) — чистые функции, без репозиториев.
/// Итоговая роль = максимум из «по умолчанию» (глобальная роль, срезанная потолком проекта) и прямого участия.
/// Запретов (deny) нет намеренно: с ними и группами получаются матрицы, которые никто не предскажет.
/// </summary>
public static class ProjectRoles
{
    private static readonly IReadOnlyDictionary<ProjectRole, IReadOnlySet<ProjectPermission>> Defaults =
        new Dictionary<ProjectRole, IReadOnlySet<ProjectPermission>>
        {
            [ProjectRole.Viewer] = new HashSet<ProjectPermission> { ProjectPermission.ViewProject },
            [ProjectRole.Member] = new HashSet<ProjectPermission>
            {
                ProjectPermission.ViewProject, ProjectPermission.CreateTask, ProjectPermission.EditOwnTask,
                ProjectPermission.Comment, ProjectPermission.Attach
            },
            [ProjectRole.Developer] = new HashSet<ProjectPermission>
            {
                ProjectPermission.ViewProject, ProjectPermission.CreateTask, ProjectPermission.EditOwnTask,
                ProjectPermission.EditAnyTask, ProjectPermission.AssignAnyone, ProjectPermission.Comment,
                ProjectPermission.Attach
            },
            [ProjectRole.Admin] = Enum.GetValues<ProjectPermission>().ToHashSet()
        };

    /// <summary>Набор прав роли. Этап 4E заменит таблицу редактируемыми наборами — проверки останутся те же.</summary>
    public static IReadOnlySet<ProjectPermission> PermissionsOf(ProjectRole role) =>
        Defaults.TryGetValue(role, out var set) ? set : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown project role.");

    /// <summary>
    /// Глобальные Admin и Owner — администраторы любого проекта. Остальным глобальная роль даёт свою ступень,
    /// но не выше <paramref name="defaultRole"/> проекта; прямое участие поднимает до своей роли.
    /// </summary>
    public static ProjectRole Effective(UserRole globalRole, ProjectRole? defaultRole, ProjectRole? memberRole)
    {
        var derived = globalRole.ToProjectRole();
        if (derived != ProjectRole.Admin && defaultRole is { } ceiling && ceiling < derived)
            derived = ceiling;

        return memberRole is { } member && member > derived ? member : derived;
    }

    public static ProjectAccessInfo Access(Guid boardId, ProjectRole role) => new(boardId, role, PermissionsOf(role));
}

/// <summary>Роль actor'а в проекте и её права — то, что получают проверки PermissionService.</summary>
public sealed record ProjectAccessInfo(Guid BoardId, ProjectRole Role, IReadOnlySet<ProjectPermission> Permissions)
{
    public bool Has(ProjectPermission permission) => Permissions.Contains(permission);
}
