using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Зеркало матриц docs/TZ_user_roles.md для показа кнопок (Flow.Api остаётся источником истины: на 403 клиент показывает тост).
/// «Я» — UserResponse текущего пользователя из UserDirectory по AppState.CurrentUserId; null — профиль ещё не загружен,
/// тогда всё, что требует прав, скрыто.
/// </summary>
public static class Permissions
{
    public static bool CanManageBoards(UserResponse? me) => me?.Role >= UserRole.Admin;

    public static bool CanCreateTask(UserResponse? me) => me?.Role >= UserRole.Member;

    /// <summary>Developer+ — любую задачу; Member — свою (создал или исполнитель).</summary>
    public static bool CanEditTask(UserResponse? me, TaskResponse task) =>
        me is not null && (me.Role >= UserRole.Developer || (me.Role == UserRole.Member && IsOwn(me, task)));

    public static bool CanAssignAnyone(UserResponse? me) => me?.Role >= UserRole.Developer;

    /// <summary>Member на своей задаче может назначить только себя — AssigneeSelect получает OnlyUserId.</summary>
    public static bool CanAssign(UserResponse? me, TaskResponse task) =>
        CanAssignAnyone(me) || (me?.Role == UserRole.Member && IsOwn(me, task));

    public static bool CanManageUsers(UserResponse? me) => me?.Role >= UserRole.Admin;

    public static bool CanEditProfile(UserResponse? me, UserResponse target) =>
        me is not null && (me.Id == target.Id || me.Role >= UserRole.Admin);

    /// <summary>Username, почта, пароль: свои — все; чужие — только Owner.</summary>
    public static bool CanEditCredentials(UserResponse? me, UserResponse target) =>
        me is not null && (me.Id == target.Id || me.Role == UserRole.Owner);

    /// <summary>Только Owner, не себя и не другого Owner (его сначала понижают).</summary>
    public static bool CanDeactivate(UserResponse? me, UserResponse target) =>
        me?.Role == UserRole.Owner && me.Id != target.Id && target.Role != UserRole.Owner;

    /// <summary>Роли для модалки «Новый человек»: Owner — любые, Admin — ниже Admin.</summary>
    public static IReadOnlyList<UserRole> CreatableRoles(UserResponse? me) => me?.Role switch
    {
        UserRole.Owner => All,
        UserRole.Admin => BelowAdmin,
        _ => []
    };

    /// <summary>Роли, которые actor может выдать цели на странице профиля (пусто — селект скрыт).</summary>
    public static IReadOnlyList<UserRole> AssignableRoles(UserResponse? me, UserResponse target)
    {
        if (me is null || me.Role < UserRole.Admin)
            return [];

        if (me.Id == target.Id)
            return All.Where(r => r <= me.Role).ToList(); // себя можно только понизить

        if (me.Role == UserRole.Admin)
            return target.Role >= UserRole.Admin ? [] : BelowAdmin;

        return All;
    }

    public static bool CanChangeRole(UserResponse? me, UserResponse target) => AssignableRoles(me, target).Count > 0;

    public static string RoleLabel(UserRole role) => role switch
    {
        UserRole.Reader => "Читатель",
        UserRole.Member => "Участник",
        UserRole.Developer => "Разработчик",
        UserRole.Admin => "Админ",
        UserRole.Owner => "Основатель",
        _ => role.ToString()
    };

    public static string RoleHint(UserRole role) => role switch
    {
        UserRole.Reader => "Читает проекты и задачи, редактирует свой профиль",
        UserRole.Member => "Создаёт задачи, ведёт свои (создатель или исполнитель), назначает себя",
        UserRole.Developer => "Любые задачи и любой исполнитель",
        UserRole.Admin => "Проекты, добавление людей, чужие профили, роли ниже Admin",
        UserRole.Owner => "Всё, включая Admin/Owner и деактивацию людей",
        _ => string.Empty
    };

    public static string StatusLabel(UserStatus status) => status switch
    {
        UserStatus.Invited => "Приглашён",
        UserStatus.Active => "Активен",
        UserStatus.Deactivated => "Деактивирован",
        _ => status.ToString()
    };

    public static string StatusColor(UserStatus status) => status switch
    {
        UserStatus.Invited => "var(--tan)",
        UserStatus.Active => "var(--sage)",
        _ => "rgba(255,255,255,0.35)"
    };

    private static readonly IReadOnlyList<UserRole> All = [UserRole.Reader, UserRole.Member, UserRole.Developer, UserRole.Admin, UserRole.Owner];

    private static readonly IReadOnlyList<UserRole> BelowAdmin = [UserRole.Reader, UserRole.Member, UserRole.Developer];

    private static bool IsOwn(UserResponse me, TaskResponse task) => task.CreatedById == me.Id || task.AssigneeId == me.Id;
}
