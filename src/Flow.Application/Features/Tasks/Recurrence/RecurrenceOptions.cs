namespace Flow.Application.Features.Tasks.Recurrence;

/// <summary>
/// Секция "Recurrence" (docs/TZ_task_model.md §9): генератор копий раз в IntervalMinutes. «Сегодня» — по TimeZone:
/// своих часовых поясов у пользователей во Flow нет, а по UTC копия «на понедельник» в Москве появлялась бы в 3 часа ночи.
/// </summary>
public sealed class RecurrenceOptions
{
    public const string SectionName = "Recurrence";

    public bool Enabled { get; set; } = true;

    public int IntervalMinutes { get; set; } = 15;

    public string TimeZone { get; set; } = "Europe/Moscow";

    public DateOnly Today(DateTime utcNow)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone));
    }
}
