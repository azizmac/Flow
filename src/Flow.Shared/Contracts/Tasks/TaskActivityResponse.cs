namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Запись журнала задачи. OldValue/NewValue — строки: для StatusChanged/AssigneeChanged — Guid (клиент резолвит
/// по статусам проекта и справочнику людей), для DueDateChanged — yyyy-MM-dd, для TitleChanged — текст,
/// для DescriptionChanged/Created — null. null у AssigneeChanged/DueDateChanged значит «не назначен»/«без срока».
/// Source/SourceUrl — изменение пришло не из интерфейса (этап 5C): «PR #42», «коммит a1b2c3d» и ссылка на хостинг.
/// </summary>
public sealed record TaskActivityResponse(
    Guid Id,
    Guid TaskId,
    Guid ActorId,
    TaskActivityType Type,
    string? OldValue,
    string? NewValue,
    DateTime CreatedAt,
    string? Source = null,
    string? SourceUrl = null);
