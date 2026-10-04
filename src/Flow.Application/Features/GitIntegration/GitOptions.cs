namespace Flow.Application.Features.GitIntegration;

/// <summary>
/// Секция "Scm". PublicBaseUrl — адрес Flow снаружи для вебхука ({PublicBaseUrl}/hooks/git/{id}): внутри контейнера
/// хост не знает, как его видит хостинг. Не задан — вебхук не создаётся, экран показывает адрес и секрет для ручной
/// настройки. Воркер доставок — WorkerEnabled/PollIntervalSeconds; объём дозагрузки истории — Backfill*.
/// </summary>
public sealed class GitOptions
{
    // Имя секции сохранено для совместимости с существующими настройками развёртывания.
    public const string SectionName = "Scm";

    public string? PublicBaseUrl { get; set; }

    public bool WorkerEnabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>Обработанные доставки старше стольких дней удаляются.</summary>
    public int DeliveryRetentionDays { get; set; } = 30;

    /// <summary>Дозагрузка истории при привязке (этап 5B): столько последних PR…</summary>
    public int BackfillPullRequests { get; set; } = 100;

    /// <summary>…коммиты ветки по умолчанию за столько дней…</summary>
    public int BackfillCommitDays { get; set; } = 30;

    /// <summary>…но не больше стольких коммитов.</summary>
    public int BackfillMaxCommits { get; set; } = 1000;

    /// <summary>Ссылка на задачу для текста на хостинге (этап 5D): /tasks/{код} ведёт и по прежнему коду.</summary>
    public string TaskUrl(string code) => $"{(PublicBaseUrl ?? "").TrimEnd('/')}/tasks/{code}";

    public string WebhookUrl(Guid repositoryId, string? fallbackBase = null)
    {
        var baseUrl = (PublicBaseUrl ?? fallbackBase ?? "").TrimEnd('/');
        return $"{baseUrl}/hooks/git/{repositoryId}";
    }
}
