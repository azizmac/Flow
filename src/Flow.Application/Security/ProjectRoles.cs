using Flow.Application.Abstractions;
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
                ProjectPermission.Attach, ProjectPermission.ManageSprints, ProjectPermission.ManageMilestones,
                ProjectPermission.ManageTaskTemplates, ProjectPermission.WriteScm
            },
            [ProjectRole.Admin] = Enum.GetValues<ProjectPermission>().ToHashSet()
        };

    /// <summary>Набор прав роли. Этап 4E заменит таблицу редактируемыми наборами — проверки останутся те же.</summary>
    public static IReadOnlySet<ProjectPermission> PermissionsOf(ProjectRole role) =>
        Defaults.TryGetValue(role, out var set) ? set : throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown project role.");

    /// <summary>
    /// Глобальные Admin и Owner — администраторы любого проекта, в том числе приватного. В открытом проекте
    /// остальным глобальная роль даёт свою ступень, но не выше <paramref name="defaultRole"/>; прямое участие
    /// поднимает до своей роли. В приватном роль есть только у участников: null — проект actor'у не виден.
    /// </summary>
    public static ProjectRole? Effective(UserRole globalRole, BoardVisibility visibility, ProjectRole? defaultRole, ProjectRole? memberRole)
    {
        var derived = globalRole.ToProjectRole();
        if (derived == ProjectRole.Admin)
            return ProjectRole.Admin;

        if (visibility == BoardVisibility.Private)
            return memberRole;

        if (defaultRole is { } ceiling && ceiling < derived)
            derived = ceiling;

        return memberRole is { } member && member > derived ? member : derived;
    }

    /// <summary>
    /// Права с наборами (этап 4E): роль — как раньше (максимум), а права — объединение прав всех источников: роли по
    /// умолчанию (в открытом проекте) и каждого участия — прямого и через группы; у участия со своим набором — права
    /// набора, иначе права роли. Запретов нет, поэтому свой набор в открытом проекте не отнимает то, что человек и так
    /// получает по роли во Flow: урезать права можно потолком проекта или приватностью.
    /// </summary>
    public static ProjectAccessInfo Resolve(UserRole globalRole, BoardAccessData data, IReadOnlyDictionary<Guid, PermissionSet> sets)
    {
        var role = Effective(globalRole, data.Visibility, data.DefaultRole, data.MemberRole);
        if (role is null)
            return Access(data.BoardId, null);
        if (globalRole.ToProjectRole() == ProjectRole.Admin)
            return Access(data.BoardId, ProjectRole.Admin);

        var permissions = new HashSet<ProjectPermission> { ProjectPermission.ViewProject };
        if (data.Visibility == BoardVisibility.Open)
        {
            var derived = globalRole.ToProjectRole();
            if (data.DefaultRole is { } ceiling && ceiling < derived)
                derived = ceiling;
            permissions.UnionWith(PermissionsOf(derived));
        }

        foreach (var grant in data.Grants)
            permissions.UnionWith(grant.PermissionSetId is { } id && sets.TryGetValue(id, out var set) ? set.Permissions : PermissionsOf(grant.Role));

        return new ProjectAccessInfo(data.BoardId, role, permissions);
    }

    public static ProjectAccessInfo Access(Guid boardId, ProjectRole? role) =>
        new(boardId, role, role is { } r ? PermissionsOf(r) : new HashSet<ProjectPermission>());
}

/// <summary>
/// Роль actor'а в проекте и её права — то, что получают проверки PermissionService. Role = null — проект
/// actor'у не виден: любая проверка отвечает 404 (ProjectNotFoundException), а не 403.
/// </summary>
public sealed record ProjectAccessInfo(Guid BoardId, ProjectRole? Role, IReadOnlySet<ProjectPermission> Permissions)
{
    public bool CanView => Role is not null;

    public bool Has(ProjectPermission permission) => Permissions.Contains(permission);
}
