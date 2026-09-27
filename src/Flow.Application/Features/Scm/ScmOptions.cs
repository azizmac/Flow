namespace Flow.Application.Features.Scm;

/// <summary>
/// Секция "Scm". PublicBaseUrl — адрес Flow снаружи для вебхука ({PublicBaseUrl}/hooks/scm/{id}): внутри контейнера
/// хост не знает, как его видит хостинг. Не задан — вебхук не создаётся, экран показывает адрес и секрет для ручной
/// настройки. Воркер доставок — WorkerEnabled/PollIntervalSeconds.
/// </summary>
public sealed class ScmOptions
{
    public const string SectionName = "Scm";

    public string? PublicBaseUrl { get; set; }

    public bool WorkerEnabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Обработанные доставки старше стольких дней удаляются.</summary>
    public int DeliveryRetentionDays { get; set; } = 30;

    public string WebhookUrl(Guid repositoryId, string? fallbackBase = null)
    {
        var baseUrl = (PublicBaseUrl ?? fallbackBase ?? "").TrimEnd('/');
        return $"{baseUrl}/hooks/scm/{repositoryId}";
    }
}
