namespace Flow.Shared.Contracts.Tasks;

// Повторяющиеся задачи (docs/TZ_task_model.md §9, этап 1F).

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}

/// <summary>
/// Правило повторения задачи-образца. WeekDays — для Weekly (пусто — день недели даты начала), MonthDay — для Monthly
/// (1…31, -1 — последний день; null — день даты начала). LeadDays — за сколько дней создавать копию, DueOffsetDays —
/// срок копии через столько дней после даты (null — без срока). IsActive = false — пауза.
/// </summary>
public sealed record TaskRecurrenceRequest(
    RecurrenceFrequency Frequency,
    int Interval,
    DateOnly StartsOn,
    IReadOnlyList<DayOfWeek>? WeekDays = null,
    int? MonthDay = null,
    DateOnly? EndsOn = null,
    int LeadDays = 0,
    int? DueOffsetDays = null,
    bool CopyAssignee = true,
    bool CopyChecklist = true,
    bool IsActive = true);

/// <summary>Правило задачи; NextDates — ближайшие даты копий (с учётом уже созданных), LastError — почему на паузе.</summary>
public sealed record TaskRecurrenceResponse(
    Guid TaskId,
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<DayOfWeek> WeekDays,
    int? MonthDay,
    DateOnly StartsOn,
    DateOnly? EndsOn,
    int LeadDays,
    int? DueOffsetDays,
    bool CopyAssignee,
    bool CopyChecklist,
    bool IsActive,
    Guid CreatedById,
    DateOnly? GeneratedUntil,
    string? LastError,
    IReadOnlyList<DateOnly> NextDates);
