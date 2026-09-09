namespace Flow.Domain.Entities;

/// <summary>
/// Роль пользователя в workspace, одна и глобальная (ролей на проект нет). Порядок = сила роли:
/// права проверяются сравнением, например <c>actor.Role &gt;= UserRole.Admin</c>. См. docs/TZ_user_roles.md.
/// </summary>
public enum UserRole
{
    /// <summary>Читает проекты, задачи, людей; редактирует свой профиль.</summary>
    Reader = 0,

    /// <summary>+ создаёт задачи; редактирует и меняет статус своих задач (создатель или исполнитель); назначает себя.</summary>
    Member = 1,

    /// <summary>+ любые задачи, любой исполнитель.</summary>
    Developer = 2,

    /// <summary>+ проекты; добавляет людей; чужие профили; роли ниже Admin.</summary>
    Admin = 3,

    /// <summary>+ назначает Owner/Admin; деактивирует и активирует людей. Минимум один в workspace.</summary>
    Owner = 4
}
