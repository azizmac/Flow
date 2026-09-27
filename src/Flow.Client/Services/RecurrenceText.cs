using Flow.Shared.Contracts.Tasks;

namespace Flow.Client.Services;

/// <summary>Подписи правила повторения (docs/TZ_task_model.md §9): «каждую неделю по пн, ср», «каждые 2 месяца, последний день».</summary>
public static class RecurrenceText
{
    /// <summary>Дни недели с понедельника — в порядке, привычном в России.</summary>
    public static readonly IReadOnlyList<DayOfWeek> Week =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    public static string Day(DayOfWeek day) => Ru.WeekdaysShort[((int)day + 6) % 7];

    public static string Frequency(RecurrenceFrequency frequency, int interval) => (frequency, interval) switch
    {
        (RecurrenceFrequency.Daily, 1) => "каждый день",
        (RecurrenceFrequency.Weekly, 1) => "каждую неделю",
        (RecurrenceFrequency.Monthly, 1) => "каждый месяц",
        (RecurrenceFrequency.Yearly, 1) => "каждый год",
        (RecurrenceFrequency.Daily, var n) => $"каждые {n} {Ru.Plural(n, "день", "дня", "дней")}",
        (RecurrenceFrequency.Weekly, var n) => $"каждые {n} {Ru.Plural(n, "неделю", "недели", "недель")}",
        (RecurrenceFrequency.Monthly, var n) => $"каждые {n} {Ru.Plural(n, "месяц", "месяца", "месяцев")}",
        (_, var n) => $"каждые {n} {Ru.Plural(n, "год", "года", "лет")}"
    };

    public static string Describe(RecurrenceFrequency frequency, int interval, IReadOnlyCollection<DayOfWeek> weekDays, int? monthDay, DateOnly startsOn)
    {
        var text = Frequency(frequency, interval);
        return frequency switch
        {
            RecurrenceFrequency.Weekly => $"{text} по {string.Join(", ", (weekDays.Count > 0 ? Week.Where(weekDays.Contains) : [startsOn.DayOfWeek]).Select(Day))}",
            RecurrenceFrequency.Monthly => $"{text}, {(monthDay == -1 ? "последний день" : $"{monthDay ?? startsOn.Day}-го")}",
            RecurrenceFrequency.Yearly => $"{text}, {Ru.DateOnlyShort(startsOn)}",
            _ => text
        };
    }

    public static string Describe(TaskRecurrenceResponse r) => Describe(r.Frequency, r.Interval, r.WeekDays, r.MonthDay, r.StartsOn);
}
