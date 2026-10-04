using Flow.Shared.Contracts.Attachments;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>
/// Зеркало матриц для показа кнопок: глобальные — по роли (docs/TZ_user_roles.md), внутри проекта — по правам
/// из ProjectAccessState (docs/TZ_project_access.md) (Flow.Api остаётся источником истины: на 403 клиент показывает тост).
/// «Я» — UserResponse текущего пользователя из UserDirectory по AppState.CurrentUserId; null — профиль ещё не загружен,
/// тогда всё, что требует прав, скрыто.
/// </summary>
public static class Permissions
{
    /// <summary>Создать проект — глобальные Admin и Owner.</summary>
    public static bool CanCreateBoard(UserResponse? me) => me?.Role >= UserRole.Admin;

    public static bool CanRenameBoard(ProjectAccessResponse? access) => Has(access, ProjectPermission.RenameProject);

    /// <summary>Удаление уносит работу всех — нужны администратор проекта и глобальный Admin+.</summary>
    public static bool CanDeleteBoard(UserResponse? me, ProjectAccessResponse? access) =>
        me?.Role >= UserRole.Admin && Has(access, ProjectPermission.DeleteProject);

    /// <summary>Типы задач и прочая настройка проекта.</summary>
    public static bool CanManageConfig(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageConfig);

    public static bool CanManageMembers(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageMembers);

    /// <summary>Спринты: создать, начать, завершить, править (docs/TZ_task_views.md §2) — Developer и выше.</summary>
    public static bool CanManageSprints(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageSprints);

    public static bool CanManageMilestones(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageMilestones);
    public static bool CanManageTaskTemplates(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageTaskTemplates);
    public static bool CanWriteGit(ProjectAccessResponse? access) => Has(access, ProjectPermission.WriteGit);

    public static bool CanManageGit(ProjectAccessResponse? access) => Has(access, ProjectPermission.ManageGit);

    public static bool CanCreateTask(ProjectAccessResponse? access) => Has(access, ProjectPermission.CreateTask);

    /// <summary>EditAnyTask — любую задачу; EditOwnTask — свою (создал или исполнитель).</summary>
    public static bool CanEditTask(UserResponse? me, ProjectAccessResponse? access, TaskResponse task) =>
        me is not null && (Has(access, ProjectPermission.EditAnyTask) || (Has(access, ProjectPermission.EditOwnTask) && IsOwn(me, task)));

    public static bool CanAssignAnyone(ProjectAccessResponse? access) => Has(access, ProjectPermission.AssignAnyone);

    /// <summary>Участник на своей задаче может назначить только себя — UserPicker получает OnlyUserId.</summary>
    public static bool CanAssign(UserResponse? me, ProjectAccessResponse? access, TaskResponse task) =>
        CanAssignAnyone(access) || (me is not null && Has(access, ProjectPermission.EditOwnTask) && IsOwn(me, task));

    public static bool CanManageUsers(UserResponse? me) => me?.Role >= UserRole.Admin;

    /// <summary>Состояние поискового индекса (GET /search/status) — Admin и Owner.</summary>
    public static bool CanViewSearchDiagnostics(UserResponse? me) => me?.Role >= UserRole.Admin;

    /// <summary>Массовая переиндексация (POST /search/reindex) — только Owner.</summary>
    public static bool CanReindex(UserResponse? me) => me?.Role >= UserRole.Owner;

    /// <summary>Комментировать — право Comment в проекте; читатель только читает.</summary>
    public static bool CanComment(ProjectAccessResponse? access) => Has(access, ProjectPermission.Comment);

    public static bool CanEditComment(UserResponse? me, ProjectAccessResponse? access, TaskCommentResponse comment) =>
        me is not null && comment.AuthorId == me.Id && CanComment(access);

    public static bool CanDeleteComment(UserResponse? me, ProjectAccessResponse? access, TaskCommentResponse comment) =>
        CanEditComment(me, access, comment) || Has(access, ProjectPermission.DeleteAnyComment);

    /// <summary>Прикладывать файлы — право Attach; скачивать может любой, кто видит проект.</summary>
    public static bool CanAttach(ProjectAccessResponse? access) => Has(access, ProjectPermission.Attach);

    /// <summary>Свой файл — автор (пока может прикладывать), чужой — администратор проекта.</summary>
    public static bool CanDeleteAttachment(UserResponse? me, ProjectAccessResponse? access, AttachmentResponse attachment) =>
        (me is not null && attachment.UploadedById == me.Id && CanAttach(access)) || Has(access, ProjectPermission.DeleteAnyAttachment);

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

    public static string ProjectRoleLabel(ProjectRole role) => role switch
    {
        ProjectRole.Viewer => "Читатель",
        ProjectRole.Member => "Участник",
        ProjectRole.Developer => "Разработчик",
        ProjectRole.Admin => "Администратор",
        _ => role.ToString()
    };

    /// <summary>Подписи прав проекта — строки матрицы наборов прав (этап 4E).</summary>
    public static string PermissionLabel(ProjectPermission permission) => permission switch
    {
        ProjectPermission.ViewProject => "Видеть проект и задачи",
        ProjectPermission.CreateTask => "Создавать задачи",
        ProjectPermission.EditOwnTask => "Править свои задачи",
        ProjectPermission.EditAnyTask => "Править любые задачи",
        ProjectPermission.AssignAnyone => "Назначать любого исполнителя",
        ProjectPermission.Comment => "Комментировать",
        ProjectPermission.Attach => "Прикреплять файлы",
        ProjectPermission.DeleteAnyComment => "Удалять чужие комментарии",
        ProjectPermission.DeleteAnyAttachment => "Удалять чужие файлы",
        ProjectPermission.ManageConfig => "Настраивать проект: статусы, workflow, типы, поля, экраны",
        ProjectPermission.ManageMembers => "Управлять участниками и доступом",
        ProjectPermission.RenameProject => "Переименовывать проект",
        ProjectPermission.DeleteProject => "Удалять проект",
        ProjectPermission.ManageSprints => "Вести спринты",
        ProjectPermission.ManageMilestones => "Вести вехи",
        ProjectPermission.ManageGit => "Привязывать репозитории",
        ProjectPermission.ManageTaskTemplates => "Вести шаблоны задач",
        ProjectPermission.WriteGit => "Создавать ветки и PR из Flow",
        _ => permission.ToString()
    };

    public static string ProjectRoleHint(ProjectRole role) => role switch
    {
        ProjectRole.Viewer => "Читает задачи проекта",
        ProjectRole.Member => "Создаёт задачи, ведёт свои, комментирует",
        ProjectRole.Developer => "Любые задачи проекта и любой исполнитель",
        ProjectRole.Admin => "Настройки, участники, переименование проекта",
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

    private static bool Has(ProjectAccessResponse? access, ProjectPermission permission) =>
        access?.Permissions.Contains(permission) == true;

    private static bool IsOwn(UserResponse me, TaskResponse task) => task.CreatedById == me.Id || task.AssigneeId == me.Id;
}
