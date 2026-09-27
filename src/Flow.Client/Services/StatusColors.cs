using Flow.Shared.Contracts.Boards;

namespace Flow.Client.Services;

/// <summary>Цвет точки/пилюли статуса по StatusType. Кастомный статус (Type = null) — danger.</summary>
public static class StatusColors
{
    public static string For(StatusType? type) => type switch
    {
        StatusType.NotStarted => "rgba(255,255,255,0.35)",
        StatusType.InProgress => "var(--accent)",
        StatusType.InReview => "var(--tan)",
        StatusType.Done => "var(--sage)",
        _ => "var(--danger)"
    };

    /// <summary>Подпись вида статуса: вид общий для всех проектов, по нему работают фильтры «Задач» и поиска.</summary>
    public static string TypeLabel(StatusType? type) => type switch
    {
        StatusType.NotStarted => "Не начата",
        StatusType.InProgress => "В работе",
        StatusType.InReview => "На проверке",
        StatusType.Done => "Сделана",
        _ => "Без вида"
    };

    public static readonly IReadOnlyList<StatusType?> TypeOptions =
        [StatusType.NotStarted, StatusType.InProgress, StatusType.InReview, StatusType.Done, null];

    /// <summary>Статуса может не быть в списке проекта (Current = null) — тогда цвет «неизвестного».</summary>
    public static string For(StatusResponse? status) => For(status?.Type);
}
