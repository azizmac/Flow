namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// Зеркало Flow.Domain.Entities.ProjectPermission. Клиент решает, какие кнопки показывать, по списку прав
/// из ProjectAccessResponse, а не по роли: таблица «роль → права» живёт только на сервере.
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
    ManageSprints = 13,

    /// <summary>Создавать, править, закрывать и удалять вехи (docs/TZ_task_views.md §6): Developer и выше.</summary>
    ManageMilestones = 14,
    ManageGit = 15,
    ManageTaskTemplates = 16,
    WriteGit = 17
}
