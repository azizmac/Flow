using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Матрицы прав из docs/TZ_user_roles.md. Каждый Ensure* бросает <see cref="Exceptions.ForbiddenException"/> (→ 403),
/// если actor'у действие не разрешено. Actor — уже загруженный и не деактивированный пользователь (см. ActorResolver).
/// Инварианты, которые зависят от других пользователей («последний Owner»), проверяют хендлеры через репозиторий.
/// </summary>
public interface IPermissionService
{
    /// <summary>Создать, переименовать, удалить проект — Admin и Owner.</summary>
    void EnsureCanManageBoards(User actor);

    /// <summary>Создать задачу — Member и выше.</summary>
    void EnsureCanCreateTask(User actor);

    /// <summary>Редактировать, удалять, менять статус: Developer+ — любую; Member — свою (создатель или исполнитель).</summary>
    void EnsureCanEditTask(User actor, TaskItem task);

    /// <summary>Назначить исполнителя: Developer+ — любого; Member — только себя на свою задачу (и снять себя).</summary>
    void EnsureCanAssign(User actor, TaskItem task, Guid? assigneeId);

    /// <summary>Комментировать задачи — Member и выше (Reader только читает).</summary>
    void EnsureCanComment(User actor);

    /// <summary>Править комментарий — только автор.</summary>
    void EnsureCanEditComment(User actor, TaskComment comment);

    /// <summary>Удалить комментарий — автор либо Admin и Owner.</summary>
    void EnsureCanDeleteComment(User actor, TaskComment comment);

    /// <summary>Добавлять людей — Admin и Owner.</summary>
    void EnsureCanManageUsers(User actor);

    /// <summary>Создать человека с ролью: не выше своей; Admin не выдаёт Admin и Owner.</summary>
    void EnsureCanCreateUser(User actor, UserRole role);

    /// <summary>Имя, должность, ссылки, аватар: свой профиль — все; чужой — Admin и Owner.</summary>
    void EnsureCanEditProfile(User actor, User target);

    /// <summary>Username, email, пароль: свои — все; чужие — только Owner.</summary>
    void EnsureCanEditCredentials(User actor, User target);

    /// <summary>Деактивировать и активировать — только Owner.</summary>
    void EnsureCanDeactivate(User actor);

    /// <summary>
    /// Сменить роль: Admin+; новая роль не выше своей; повысить себя нельзя; Admin — только цели ниже Admin и на роли ниже Admin.
    /// Проверка «последний Owner» — в хендлере (нужен репозиторий).
    /// </summary>
    void EnsureCanChangeRole(User actor, User target, UserRole newRole);
}
