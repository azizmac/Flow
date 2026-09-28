using Flow.Domain.Entities;

namespace Flow.Application.Security;

/// <summary>Русские имена ролей проекта — имена встроенных наборов прав (этап 4E).</summary>
public static class RoleNames
{
    public static string Of(ProjectRole role) => role switch
    {
        ProjectRole.Viewer => "Читатель",
        ProjectRole.Member => "Участник",
        ProjectRole.Developer => "Разработчик",
        ProjectRole.Admin => "Администратор",
        _ => role.ToString()
    };
}
