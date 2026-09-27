using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>
/// Матрицы прав: глобальные (люди, создание проектов, поиск) — по UserRole (docs/TZ_user_roles.md), всё внутри
/// проекта — по правам роли в проекте (ProjectAccessInfo, docs/TZ_project_access.md). Роль в проекте считает
/// IProjectAccess; без участников и потолка она равна глобальной, поэтому поведение прежнее.
/// </summary>
internal sealed class PermissionService : IPermissionService
{
    public void EnsureCanCreateBoard(User actor) =>
        Require(actor.Role >= UserRole.Admin, "Создавать проекты могут Admin и Owner.");

    public void EnsureCanRenameBoard(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.RenameProject, "Переименовать проект может его администратор.");
    }

    // Удаление уносит задачи всех участников — нужны оба уровня: администратор проекта и глобальный Admin+.
    public void EnsureCanDeleteBoard(User actor, ProjectAccessInfo access)
    {
        RequireVisible(access);
        Require(actor.Role >= UserRole.Admin, "Удалять проекты могут Admin и Owner.");
        RequireProject(access, ProjectPermission.DeleteProject, "Удалить проект может его администратор.");
    }

    public void EnsureCanManageConfig(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.ManageConfig, "Настраивать проект (типы задач) может его администратор.");
    }

    public void EnsureCanManageSprints(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.ManageSprints, "Спринты ведут разработчики и администраторы проекта.");
    }

    public void EnsureCanManageMilestones(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.ManageMilestones, "Вехи ведут разработчики и администраторы проекта.");
    }

    public void EnsureCanManageMembers(ProjectAccessInfo access, ProjectRole? grantedRole = null)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.ManageMembers, "Управлять участниками проекта может его администратор.");
        if (grantedRole is { } role)
            Require(role <= access.Role, "Нельзя выдать роль в проекте выше своей.");
    }

    public void EnsureCanCreateTask(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.CreateTask, "Читатель проекта не может создавать задачи.");
    }

    public void EnsureCanEditTask(User actor, ProjectAccessInfo access, TaskItem task)
    {
        RequireVisible(access);
        Require(access.Has(ProjectPermission.EditAnyTask) || (access.Has(ProjectPermission.EditOwnTask) && IsOwn(actor, task)),
            "Редактировать чужие задачи проекта могут разработчики и администраторы.");
    }

    public void EnsureCanAssign(User actor, ProjectAccessInfo access, TaskItem task, Guid? assigneeId)
    {
        RequireVisible(access);
        if (access.Has(ProjectPermission.AssignAnyone))
            return;

        Require(access.Has(ProjectPermission.EditOwnTask) && IsOwn(actor, task),
            "Назначать исполнителя на чужие задачи могут разработчики и администраторы проекта.");

        // Участник: назначить себя или снять себя — но не переназначать других.
        var assignsSelf = assigneeId == actor.Id;
        var unassignsSelf = assigneeId is null && task.AssigneeId == actor.Id;
        Require(assignsSelf || unassignsSelf, "Участник проекта может назначить исполнителем только себя.");
    }

    public void EnsureCanComment(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.Comment, "Читатель проекта не может комментировать задачи.");
    }

    // Автор, которого понизили до читателя, свой комментарий уже не правит: право комментировать нужно и здесь.
    public void EnsureCanEditComment(User actor, ProjectAccessInfo access, TaskComment comment)
    {
        RequireVisible(access);
        Require(comment.AuthorId == actor.Id && access.Has(ProjectPermission.Comment), "Править можно только свои комментарии.");
    }

    public void EnsureCanDeleteComment(User actor, ProjectAccessInfo access, TaskComment comment)
    {
        RequireVisible(access);
        Require((comment.AuthorId == actor.Id && access.Has(ProjectPermission.Comment)) || access.Has(ProjectPermission.DeleteAnyComment),
            "Удалять чужие комментарии может администратор проекта.");
    }

    public void EnsureCanManageUsers(User actor) =>
        Require(actor.Role >= UserRole.Admin, "Добавлять людей могут Admin и Owner.");

    public void EnsureCanCreateUser(User actor, UserRole role)
    {
        EnsureCanManageUsers(actor);
        Require(role <= actor.Role, "Нельзя выдать роль выше своей.");
        Require(actor.Role == UserRole.Owner || role < UserRole.Admin, "Назначать Admin и Owner может только Owner.");
    }

    public void EnsureCanEditProfile(User actor, User target) =>
        Require(actor.Id == target.Id || actor.Role >= UserRole.Admin, "Редактировать чужой профиль могут Admin и Owner.");

    public void EnsureCanEditCredentials(User actor, User target) =>
        Require(actor.Id == target.Id || actor.Role == UserRole.Owner, "Менять чужие username, почту и пароль может только Owner.");

    public void EnsureCanDeactivate(User actor) =>
        Require(actor.Role == UserRole.Owner, "Деактивировать и активировать людей может только Owner.");

    public void EnsureCanChangeRole(User actor, User target, UserRole newRole)
    {
        Require(actor.Role >= UserRole.Admin, "Менять роли могут Admin и Owner.");
        Require(newRole <= actor.Role, "Нельзя выдать роль выше своей.");

        if (actor.Id == target.Id)
            Require(newRole <= actor.Role, "Повысить себя нельзя.");

        if (actor.Role == UserRole.Admin)
        {
            Require(target.Role < UserRole.Admin, "Admin не может менять роль другим Admin и Owner.");
            Require(newRole < UserRole.Admin, "Назначать Admin и Owner может только Owner.");
        }
    }

    public void EnsureCanAttach(ProjectAccessInfo access)
    {
        RequireVisible(access);
        RequireProject(access, ProjectPermission.Attach, "Читатель проекта не может прикладывать файлы.");
    }

    public void EnsureCanDeleteAttachment(User actor, ProjectAccessInfo access, Attachment attachment)
    {
        RequireVisible(access);
        Require((attachment.UploadedById == actor.Id && access.Has(ProjectPermission.Attach)) || access.Has(ProjectPermission.DeleteAnyAttachment),
            "Удалять чужие вложения может администратор проекта.");
    }

    public void EnsureCanViewSearchDiagnostics(User actor) =>
        Require(actor.Role >= UserRole.Admin, "Состояние поискового индекса доступно Admin и Owner.");

    public void EnsureCanReindex(User actor) =>
        Require(actor.Role == UserRole.Owner, "Запускать переиндексацию поиска может только Owner.");

    public void EnsureCanEditSavedFilter(User actor, SavedFilter filter) =>
        Require(filter.OwnerId == actor.Id, "Менять фильтр может только его автор.");

    public void EnsureCanDeleteSavedFilter(User actor, SavedFilter filter) =>
        Require(filter.OwnerId == actor.Id || (filter.Visibility == SavedFilterVisibility.Shared && actor.Role >= UserRole.Admin),
            "Удалить фильтр может его автор, а общий — ещё Admin и Owner.");

    /// <summary>«Своя задача» для Member: создал или назначен исполнителем.</summary>
    private static bool IsOwn(User actor, TaskItem task) =>
        task.CreatedById == actor.Id || task.AssigneeId == actor.Id;

    /// <summary>Невидимый проект — 404 раньше любых проверок прав: 403 подтвердил бы, что проект существует.</summary>
    private static void RequireVisible(ProjectAccessInfo access)
    {
        if (!access.CanView)
            throw new ProjectNotFoundException();
    }

    private static void RequireProject(ProjectAccessInfo access, ProjectPermission permission, string message) =>
        Require(access.Has(permission), message);

    private static void Require(bool allowed, string message)
    {
        if (!allowed)
            throw new ForbiddenException(message);
    }
}
