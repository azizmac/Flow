namespace Flow.Domain.Entities;

/// <summary>
/// Видимость проекта (docs/TZ_project_access.md, этап 4B). Open — видят все по роли во Flow (как было всегда),
/// Private — только участники и глобальные Admin/Owner. Хранится как int — только дописывать.
/// </summary>
public enum BoardVisibility
{
    Open = 0,
    Private = 1
}
