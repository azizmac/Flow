using Flow.Application.Security;
using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Матрицы прав из docs/TZ_user_roles.md. Каждый Ensure* бросает <see cref="Exceptions.ForbiddenException"/> (→ 403),
/// если actor'у действие не разрешено. Actor — уже загруженный и не деактивированный пользователь (см. ActorResolver).
/// Инварианты, которые зависят от других пользователей («последний Owner»), проверяют хендлеры через репозиторий.
/// </summary>
public interface IPermissionService
{
    /// <summary>Создать проект — глобальные Admin и Owner.</summary>
    void EnsureCanCreateBoard(User actor);

    /// <summary>Переименовать проект — RenameProject (администратор проекта).</summary>
    void EnsureCanRenameBoard(ProjectAccessInfo access);

    /// <summary>Удалить проект — глобальный Admin+ и DeleteProject в проекте.</summary>
    void EnsureCanDeleteBoard(User actor, ProjectAccessInfo access);

    /// <summary>Типы задач и прочая настройка проекта — ManageConfig (администратор проекта).</summary>
    void EnsureCanManageConfig(ProjectAccessInfo access);

    /// <summary>Участники и роль по умолчанию — ManageMembers; выдаваемая роль не выше своей роли в проекте.</summary>
    void EnsureCanManageMembers(ProjectAccessInfo access, ProjectRole? grantedRole = null);

    /// <summary>Создать задачу — CreateTask (участник и выше).</summary>
    void EnsureCanCreateTask(ProjectAccessInfo access);

    /// <summary>Редактировать, удалять, менять статус: EditAnyTask — любую; EditOwnTask — свою (создатель или исполнитель).</summary>
    void EnsureCanEditTask(User actor, ProjectAccessInfo access, TaskItem task);

    /// <summary>Назначить исполнителя: AssignAnyone — любого; иначе только себя на свою задачу (и снять себя).</summary>
    void EnsureCanAssign(User actor, ProjectAccessInfo access, TaskItem task, Guid? assigneeId);

    /// <summary>Комментировать задачи — Comment (читатель только читает).</summary>
    void EnsureCanComment(ProjectAccessInfo access);

    /// <summary>Править комментарий — автор, если у него осталось право комментировать.</summary>
    void EnsureCanEditComment(User actor, ProjectAccessInfo access, TaskComment comment);

    /// <summary>Удалить комментарий — автор либо DeleteAnyComment (администратор проекта).</summary>
    void EnsureCanDeleteComment(User actor, ProjectAccessInfo access, TaskComment comment);

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

    /// <summary>Приложить файл к задаче — Attach (читатель только смотрит и скачивает).</summary>
    void EnsureCanAttach(ProjectAccessInfo access);

    /// <summary>Удалить вложение — тот, кто приложил, либо DeleteAnyAttachment (администратор проекта).</summary>
    void EnsureCanDeleteAttachment(User actor, ProjectAccessInfo access, Attachment attachment);

    /// <summary>Состояние поискового индекса (GET /search/status) — Admin и Owner: это эксплуатация, не поиск.</summary>
    void EnsureCanViewSearchDiagnostics(User actor);

    /// <summary>Массовая переиндексация (POST /search/reindex) — только Owner: она грузит модель и БД надолго.</summary>
    void EnsureCanReindex(User actor);

    /// <summary>Править сохранённый фильтр — только владелец.</summary>
    void EnsureCanEditSavedFilter(User actor, SavedFilter filter);

    /// <summary>Удалить — владелец; чужой общий — ещё и Admin+ (уборка брошенных общих фильтров).</summary>
    void EnsureCanDeleteSavedFilter(User actor, SavedFilter filter);
}
