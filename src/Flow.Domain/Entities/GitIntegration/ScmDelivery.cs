namespace Flow.Domain.Entities.GitIntegration;

/// <summary>
/// Доставка вебхука: принята, подпись сошлась, событие уже нормализовано до нужных Flow полей (Payload — JSON
/// нормализованного события, а не исходный мегабайтный push). Обрабатывает воркер, повтор с backoff до MaxAttempts.
/// </summary>
public sealed class ScmDelivery
{
    /// <summary>
    /// Не вебхук, а задание дозагрузки истории (этап 5B): последние PR и коммиты ветки по умолчанию через API. Живёт в
    /// той же очереди — тот же воркер, повторы, диагностика и срок хранения.
    /// </summary>
    public const string BackfillEvent = "flow:backfill";

    public const int MaxAttempts = 8;
    public const int ErrorMaxLength = 1000;
    public const int DeliveryIdMaxLength = 100;

    public Guid Id { get; private set; }

    public Guid RepositoryId { get; private set; }

    public string DeliveryId { get; private set; } = string.Empty;

    public string Event { get; private set; } = string.Empty;

    public DateTime ReceivedAt { get; private set; }

    public ScmDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public string Payload { get; private set; } = "{}";

    private ScmDelivery()
    {
        // EF Core
    }

    public static ScmDelivery Create(Guid repositoryId, string deliveryId, string eventName, string? payload)
    {
        var now = DateTime.UtcNow;
        var id = string.IsNullOrWhiteSpace(deliveryId) ? Guid.NewGuid().ToString() : deliveryId.Trim();
        return new ScmDelivery
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            DeliveryId = id.Length <= DeliveryIdMaxLength ? id : id[..DeliveryIdMaxLength],
            Event = eventName.Length <= 60 ? eventName : eventName[..60],
            ReceivedAt = now,
            NextAttemptAt = now,
            Status = payload is null ? ScmDeliveryStatus.Ignored : ScmDeliveryStatus.Pending,
            Payload = payload ?? "{}"
        };
    }

    public bool IsBackfill => Event == BackfillEvent;

    /// <summary>Задание дозагрузки истории репозитория — Pending сразу.</summary>
    public static ScmDelivery CreateBackfill(Guid repositoryId) =>
        Create(repositoryId, $"backfill-{Guid.NewGuid():N}", BackfillEvent, "{}");

    /// <summary>
    /// Отложить без траты попытки: хостинг исчерпал лимит запросов — это не сбой, а пауза до сброса лимита.
    /// </summary>
    public void Postpone(DateTime until, string reason)
    {
        if (Status != ScmDeliveryStatus.Pending)
            throw new InvalidOperationException("Отложить можно только доставку в очереди.");
        NextAttemptAt = DateTime.SpecifyKind(until, DateTimeKind.Utc);
        LastError = reason.Length <= ErrorMaxLength ? reason : reason[..ErrorMaxLength];
    }

    /// <summary>Повтор вручную (диагностика доставок): Failed снова в очередь, попытки с нуля.</summary>
    public void Retry(DateTime utcNow)
    {
        if (Status != ScmDeliveryStatus.Failed)
            throw new InvalidOperationException("Повторить можно только доставку с ошибкой.");
        Status = ScmDeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = utcNow;
        LastError = null;
    }

    public void MarkDone()
    {
        Status = ScmDeliveryStatus.Done;
        Attempts++;
        LastError = null;
    }

    /// <summary>Сбой обработки: повтор с backoff 5 с → 5 мин, после MaxAttempts — Failed.</summary>
    public void MarkFailed(string error, DateTime utcNow)
    {
        Attempts++;
        LastError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        if (Attempts >= MaxAttempts)
        {
            Status = ScmDeliveryStatus.Failed;
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Attempts - 1)));
        NextAttemptAt = utcNow + delay;
    }
}
