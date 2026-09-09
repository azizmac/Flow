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

    public static string For(StatusResponse status) => For(status.Type);
}
