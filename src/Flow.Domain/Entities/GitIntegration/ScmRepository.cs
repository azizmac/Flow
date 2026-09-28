namespace Flow.Domain.Entities.GitIntegration;

/// <summary>Репозиторий подключения, с которого Flow принимает вебхуки. Секрет вебхука зашифрован.</summary>
public sealed class ScmRepository
{
    public const int NameMaxLength = 300;

    public Guid Id { get; private set; }

    public Guid ConnectionId { get; private set; }

    /// <summary>Id у хостинга: у GitHub/Gitea — число, у GitLab — id проекта. Строкой.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>org/repo.</summary>
    public string FullName { get; private set; } = string.Empty;

    public string WebUrl { get; private set; } = string.Empty;

    public string DefaultBranch { get; private set; } = "main";

    /// <summary>Id вебхука у хостинга; null — создать не удалось, настроен вручную.</summary>
    public string? WebhookId { get; private set; }

    public string WebhookSecretProtected { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastDeliveryAt { get; private set; }

    private ScmRepository()
    {
        // EF Core
    }

    public static ScmRepository Create(Guid connectionId, string externalId, string fullName, string webUrl, string? defaultBranch, string webhookSecretProtected)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id must not be empty.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(fullName) || fullName.Length > NameMaxLength)
            throw new ArgumentException("Репозиторий без id или имени.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(webhookSecretProtected))
            throw new ArgumentException("Нужен секрет вебхука.", nameof(webhookSecretProtected));

        return new ScmRepository
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            ExternalId = externalId.Trim(),
            FullName = fullName.Trim(),
            WebUrl = ScmConnection.NormalizeUrl(webUrl) ?? throw new ArgumentException("Нет адреса репозитория.", nameof(webUrl)),
            DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim(),
            WebhookSecretProtected = webhookSecretProtected,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetWebhook(string? webhookId) => WebhookId = string.IsNullOrWhiteSpace(webhookId) ? null : webhookId;

    public void SetDefaultBranch(string? branch)
    {
        if (!string.IsNullOrWhiteSpace(branch))
            DefaultBranch = branch.Trim();
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Повторное подключение: новый секрет вебхука, приём включён. Связи задач прежнего подключения остаются.</summary>
    public void Reactivate(string webhookSecretProtected)
    {
        if (string.IsNullOrWhiteSpace(webhookSecretProtected))
            throw new ArgumentException("Нужен секрет вебхука.", nameof(webhookSecretProtected));
        WebhookSecretProtected = webhookSecretProtected;
        WebhookId = null;
        IsActive = true;
    }

    public void MarkDelivery(DateTime utcNow) => LastDeliveryAt = utcNow;
}
