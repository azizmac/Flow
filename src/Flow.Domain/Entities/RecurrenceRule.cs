namespace Flow.Domain.Entities;

public enum RecurrenceFrequency
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    Yearly = 3
}

/// <summary>
/// Правило повторения (docs/TZ_task_model.md §9) — подмножество RRULE полями, а не строкой: полный RFC 5545
/// интерфейсу не нужен, а строку пришлось бы разбирать. Интервал считается от даты начала: «каждые 2 недели» —
/// недели 0, 2, 4… от недели StartsOn (неделя с понедельника). День месяца больше длины месяца и 29 февраля в
/// невисокосный год сдвигаются на последний день месяца — задача не пропадает из месяца молча.
/// Value object: в EF — complex type, колонки Rule* прямо в TaskRecurrences.
/// </summary>
public sealed class RecurrenceRule
{
    public const int MaxInterval = 99;
    /// <summary>ByMonthDay = -1 — последний день месяца.</summary>
    public const int LastDayOfMonth = -1;

    public RecurrenceFrequency Frequency { get; private set; }

    /// <summary>Каждые N дней/недель/месяцев/лет, 1…99.</summary>
    public int Interval { get; private set; }

    /// <summary>Дни недели для Weekly — битовая маска по DayOfWeek (бит 0 — воскресенье); 0 — день недели даты начала.</summary>
    public int WeekDays { get; private set; }

    /// <summary>День месяца для Monthly: 1…31 или -1 (последний); null — день даты начала.</summary>
    public int? MonthDay { get; private set; }

    private RecurrenceRule()
    {
        // EF Core
    }

    public static RecurrenceRule Create(RecurrenceFrequency frequency, int interval = 1, IEnumerable<DayOfWeek>? weekDays = null, int? monthDay = null)
    {
        if (!Enum.IsDefined(frequency))
            throw new ArgumentException($"Unknown frequency {frequency}.", nameof(frequency));
        if (interval is < 1 or > MaxInterval)
            throw new ArgumentException($"Интервал — от 1 до {MaxInterval}.", nameof(interval));

        var mask = 0;
        foreach (var day in weekDays ?? [])
        {
            if (!Enum.IsDefined(day))
                throw new ArgumentException($"Unknown day {day}.", nameof(weekDays));
            mask |= 1 << (int)day;
        }

        if (monthDay is { } d && d != LastDayOfMonth && d is < 1 or > 31)
            throw new ArgumentException("День месяца — от 1 до 31 или «последний».", nameof(monthDay));

        return new RecurrenceRule
        {
            Frequency = frequency,
            Interval = interval,
            WeekDays = frequency == RecurrenceFrequency.Weekly ? mask : 0,
            MonthDay = frequency == RecurrenceFrequency.Monthly ? monthDay : null
        };
    }

    public IReadOnlyList<DayOfWeek> Days =>
        Enum.GetValues<DayOfWeek>().Where(d => (WeekDays & (1 << (int)d)) != 0).ToList();

    /// <summary>Даты правила в окне [from, to], не раньше начала <paramref name="startsOn"/>, по возрастанию.</summary>
    public IEnumerable<DateOnly> Occurrences(DateOnly startsOn, DateOnly from, DateOnly to)
    {
        if (from < startsOn)
            from = startsOn;
        if (to < from)
            yield break;

        switch (Frequency)
        {
            case RecurrenceFrequency.Daily:
            {
                var offset = from.DayNumber - startsOn.DayNumber;
                var first = startsOn.AddDays((offset + Interval - 1) / Interval * Interval);
                for (var date = first; date <= to; date = date.AddDays(Interval))
                    yield return date;
                break;
            }
            case RecurrenceFrequency.Weekly:
            {
                var days = WeekDays == 0 ? 1 << (int)startsOn.DayOfWeek : WeekDays;
                var startWeek = WeekStart(startsOn);
                for (var week = WeekStart(from); week <= to; week = week.AddDays(7))
                {
                    if ((week.DayNumber - startWeek.DayNumber) / 7 % Interval != 0)
                        continue;
                    for (var i = 0; i < 7; i++)
                    {
                        var date = week.AddDays(i);
                        if (date >= from && date <= to && (days & (1 << (int)date.DayOfWeek)) != 0)
                            yield return date;
                    }
                }
                break;
            }
            case RecurrenceFrequency.Monthly:
            {
                var day = MonthDay ?? startsOn.Day;
                for (var month = MonthIndex(from) - (MonthIndex(from) - MonthIndex(startsOn)) % Interval; ; month += Interval)
                {
                    var year = month / 12;
                    var m = month % 12 + 1;
                    var last = DateTime.DaysInMonth(year, m);
                    var date = new DateOnly(year, m, day == LastDayOfMonth ? last : Math.Min(day, last));
                    if (date > to)
                        break;
                    if (date >= from)
                        yield return date;
                }
                break;
            }
            default:
            {
                for (var year = from.Year - (from.Year - startsOn.Year) % Interval; ; year += Interval)
                {
                    var date = new DateOnly(year, startsOn.Month, Math.Min(startsOn.Day, DateTime.DaysInMonth(year, startsOn.Month)));
                    if (date > to)
                        break;
                    if (date >= from)
                        yield return date;
                }
                break;
            }
        }
    }

    private static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static int MonthIndex(DateOnly date) => date.Year * 12 + date.Month - 1;
}
