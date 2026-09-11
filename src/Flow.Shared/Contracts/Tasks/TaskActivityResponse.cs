namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Запись журнала задачи. OldValue/NewValue — строки: для StatusChanged/AssigneeChanged — Guid (клиент резолвит
/// по статусам проекта и справочнику людей), для DueDateChanged — yyyy-MM-dd, для TitleChanged — текст,
/// для DescriptionChanged/Created — null. null у AssigneeChanged/DueDateChanged значит «не назначен»/«без срока».
/// </summary>
public sealed record TaskActivityResponse(
    Guid Id,
    Guid TaskId,
    Guid ActorId,
    TaskActivityType Type,
    string? OldValue,
    string? NewValue,
    DateTime CreatedAt);
