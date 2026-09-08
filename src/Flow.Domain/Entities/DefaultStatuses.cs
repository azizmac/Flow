namespace Flow.Domain.Entities;

/// <summary>
/// Готовый набор статусов, которым Board.Create засеивает новую доску. Вынесен из тела Create отдельным
/// классом, чтобы не хардкодить магические строки/флаги внутри метода и чтобы набор был переиспользуемым.
/// </summary>
public static class DefaultStatuses
{
    public static readonly IReadOnlyList<StatusDefinition> All =
    [
        new(StatusType.NotStarted, "Не начата", IsInitial: true, IsFinal: false),
        new(StatusType.InProgress, "В работе", IsInitial: false, IsFinal: false),
        new(StatusType.InReview, "На проверке", IsInitial: false, IsFinal: false),
        new(StatusType.Done, "Сделана", IsInitial: false, IsFinal: true)
    ];

    public sealed record StatusDefinition(StatusType Type, string Name, bool IsInitial, bool IsFinal);
}
