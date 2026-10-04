using System.Text.RegularExpressions;

namespace Flow.Domain.Entities.GitIntegration;

/// <summary>Репозиторий подключения, с которого Flow принимает вебхуки. Секрет вебхука зашифрован.</summary>
public sealed partial class GitRepository
{
    public const int CommitMaxLength = 128;
    public const int SyncErrorMaxLength = 1000;

    public const int NameMaxLength = 300;

    [GeneratedRegex("^[0-9a-fA-F]{7,128}$")]
    private static partial Regex CommitPattern();

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

    /// <summary>Состояние локальной копии; не влияет на приём вебхуков.</summary>
    public GitWorkspaceSyncState SyncState { get; private set; } = GitWorkspaceSyncState.Pending;

    public string? LastSyncedCommit { get; private set; }

    public DateTime? LastSyncedAt { get; private set; }

    public string? LastSyncError { get; private set; }

    private GitRepository()
    {
        // EF Core
    }

    public static GitRepository Create(Guid connectionId, string externalId, string fullName, string webUrl, string? defaultBranch, string webhookSecretProtected)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id must not be empty.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(fullName) || fullName.Length > NameMaxLength)
            throw new ArgumentException("Репозиторий без id или имени.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(webhookSecretProtected))
            throw new ArgumentException("Нужен секрет вебхука.", nameof(webhookSecretProtected));

        return new GitRepository
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            ExternalId = externalId.Trim(),
            FullName = fullName.Trim(),
            WebUrl = GitHostConnection.NormalizeUrl(webUrl) ?? throw new ArgumentException("Нет адреса репозитория.", nameof(webUrl)),
            DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim(),
            WebhookSecretProtected = webhookSecretProtected,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetWebhook(string? webhookId) => WebhookId = string.IsNullOrWhiteSpace(webhookId) ? null : webhookId;

    public void SetDefaultBranch(string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch) || DefaultBranch == branch.Trim())
            return;

        DefaultBranch = branch.Trim();
        SyncState = GitWorkspaceSyncState.Pending;
        LastSyncedCommit = null;
        LastSyncedAt = null;
        LastSyncError = null;
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

    public void StartSync()
    {
        if (SyncState == GitWorkspaceSyncState.Syncing)
            throw new InvalidOperationException("Repository is already synchronizing.");
        SyncState = GitWorkspaceSyncState.Syncing;
        LastSyncError = null;
    }

    public void CompleteSync(string commit)
    {
        var normalized = commit?.Trim() ?? string.Empty;
        if (!CommitPattern().IsMatch(normalized))
            throw new ArgumentException("Commit hash is invalid.", nameof(commit));
        LastSyncedCommit = normalized.ToLowerInvariant();
        LastSyncedAt = DateTime.UtcNow;
        LastSyncError = null;
        SyncState = GitWorkspaceSyncState.Ready;
    }

    public void FailSync(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Sync error must not be empty.", nameof(error));
        LastSyncError = error.Trim()[..Math.Min(error.Trim().Length, SyncErrorMaxLength)];
        SyncState = GitWorkspaceSyncState.Failed;
    }
}
