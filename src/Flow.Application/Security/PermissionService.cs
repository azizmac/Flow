using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>Матрицы прав docs/TZ_user_roles.md. Сравнение ролей — по порядку enum (Reader &lt; … &lt; Owner).</summary>
internal sealed class PermissionService : IPermissionService
{
    public void EnsureCanManageBoards(User actor) =>
        Require(actor.Role >= UserRole.Admin, "Создавать, переименовывать и удалять проекты могут Admin и Owner.");

    public void EnsureCanCreateTask(User actor) =>
        Require(actor.Role >= UserRole.Member, "Reader не может создавать задачи.");

    public void EnsureCanEditTask(User actor, TaskItem task) =>
        Require(actor.Role >= UserRole.Developer || (actor.Role == UserRole.Member && IsOwn(actor, task)),
            "Редактировать чужие задачи могут Developer и выше.");

    public void EnsureCanAssign(User actor, TaskItem task, Guid? assigneeId)
    {
        if (actor.Role >= UserRole.Developer)
            return;

        Require(actor.Role == UserRole.Member && IsOwn(actor, task), "Назначать исполнителя на чужие задачи могут Developer и выше.");

        // Member: назначить себя или снять себя — но не переназначать других.
        var assignsSelf = assigneeId == actor.Id;
        var unassignsSelf = assigneeId is null && task.AssigneeId == actor.Id;
        Require(assignsSelf || unassignsSelf, "Member может назначить исполнителем только себя.");
    }

    public void EnsureCanComment(User actor) =>
        Require(actor.Role >= UserRole.Member, "Reader не может комментировать задачи.");

    public void EnsureCanEditComment(User actor, TaskComment comment) =>
        Require(comment.AuthorId == actor.Id, "Править можно только свои комментарии.");

    public void EnsureCanDeleteComment(User actor, TaskComment comment) =>
        Require(comment.AuthorId == actor.Id || actor.Role >= UserRole.Admin, "Удалять чужие комментарии могут Admin и Owner.");

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

    /// <summary>«Своя задача» для Member: создал или назначен исполнителем.</summary>
    private static bool IsOwn(User actor, TaskItem task) =>
        task.CreatedById == actor.Id || task.AssigneeId == actor.Id;

    private static void Require(bool allowed, string message)
    {
        if (!allowed)
            throw new ForbiddenException(message);
    }
}
