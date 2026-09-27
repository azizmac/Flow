namespace Flow.Domain.Entities;

/// <summary>
/// Право внутри проекта. Проверки идут по праву, а роль — лишь именованный набор прав (ProjectRoleDefaults
/// в Flow.Application): редактируемые наборы (этап 4E) заменят таблицу, не трогая проверок.
/// Хранится и передаётся как int — только дописывать. Удаление задачи — это правка (EditOwnTask/EditAnyTask),
/// как и раньше: Member удаляет свою задачу.
/// </summary>
public enum ProjectPermission
{
    ViewProject = 0,
    CreateTask = 1,
    EditOwnTask = 2,
    EditAnyTask = 3,
    AssignAnyone = 4,
    Comment = 5,
    Attach = 6,
    DeleteAnyComment = 7,
    DeleteAnyAttachment = 8,
    ManageConfig = 9,
    ManageMembers = 10,
    RenameProject = 11,
    DeleteProject = 12,

    /// <summary>Создавать, запускать и завершать спринты (docs/TZ_task_views.md §2): Developer и выше.</summary>
    ManageSprints = 13
}
