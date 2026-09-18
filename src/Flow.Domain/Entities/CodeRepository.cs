using System.Text.RegularExpressions;

namespace Flow.Domain.Entities;

/// <summary>
/// Git-репозиторий, подключённый к проекту (<see cref="Board"/>).
/// Один проект может иметь несколько репозиториев.
/// </summary>
public sealed partial class CodeRepository
{
    public const int NameMaxLength = 200;
    public const int RemoteUrlMaxLength = 500;
    public const int BranchMaxLength = 255;
    public const int CommitMaxLength = 128;
    public const int SyncErrorMaxLength = 1000;

    [GeneratedRegex("^[0-9a-fA-F]{7,128}$")]
    private static partial Regex CommitPattern();

    public Guid Id { get; private set; }

    /// <summary>Проект, которому принадлежит репозиторий.</summary>
    public Guid BoardId { get; private set; }

    public RepositoryProvider Provider { get; private set; }

    /// <summary>Понятное пользователю имя, например «PROJECT backend».</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// HTTPS-адрес Git remote без credential, query string и fragment.
    /// Например https://github.com/flow-org/flow.
    /// </summary>
    public string RemoteUrl { get; private set; } = string.Empty;

    /// <summary>Ветка репозитория.</summary>
    public string Branch { get; private set; } = string.Empty;

    public RepositorySyncState SyncState { get; private set; }

    /// <summary>
    /// Последний успешно синхронизированный commit. Не очищается при неудачном следующем обновлении,
    /// поэтому анализ может продолжаться по проверенной ревизии.
    /// </summary>
    public string? LastSyncedCommit { get; private set; }

    public DateTime? LastSyncedAt { get; private set; }

    /// <summary>Безопасное краткое сообщение для UI; технические детали остаются в логах.</summary>
    public string? LastSyncError { get; private set; }

    /// <summary>
    /// Дата создания сущности (Не Git-репозитория!)
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    private CodeRepository()
    {
        // EF Core
    }

    public CodeRepository(Guid boardId, RepositoryProvider provider, string name, string remoteUrl, string branch)
    {
        if (boardId == Guid.Empty)
            throw new ArgumentException("Board id must not be empty.", nameof(boardId));

        Id = Guid.NewGuid();
        BoardId = boardId;
        Provider = provider;
        Name = ValidateName(name);
        RemoteUrl = ValidateRemoteUrl(provider, remoteUrl);
        Branch = ValidateBranch(branch);
        SyncState = RepositorySyncState.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string name) => Name = ValidateName(name);

    /// <summary>Смена ветки требует новой синхронизации, поэтому готовая ревизия больше не считается актуальной.</summary>
    public void ChangeBranch(string branch)
    {
        Branch = ValidateBranch(branch);
        SyncState = RepositorySyncState.Pending;
        LastSyncError = null;
    }

    public void StartSync()
    {
        if (SyncState == RepositorySyncState.Disabled)
            throw new InvalidOperationException("Disabled repository cannot be synchronized.");

        SyncState = RepositorySyncState.Syncing;
        LastSyncError = null;
    }

    public void CompleteSync(string commit)
    {
        LastSyncedCommit = ValidateCommit(commit);
        LastSyncedAt = DateTime.UtcNow;
        LastSyncError = null;
        SyncState = RepositorySyncState.Ready;
    }

    public void FailSync(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("Sync error must not be empty.", nameof(error));

        LastSyncError = error.Trim()[..Math.Min(error.Trim().Length, SyncErrorMaxLength)];
        SyncState = RepositorySyncState.Failed;
    }

    public void Disable()
    {
        SyncState = RepositorySyncState.Disabled;
        LastSyncError = null;
    }

    public void Enable()
    {
        if (SyncState != RepositorySyncState.Disabled)
            throw new InvalidOperationException("Only disabled repository can be enabled.");

        SyncState = RepositorySyncState.Pending;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Repository name must not be empty.", nameof(name));

        var normalized = name.Trim();
        if (normalized.Length > NameMaxLength)
            throw new ArgumentException($"Repository name must not exceed {NameMaxLength} characters.", nameof(name));

        return normalized;
    }

    private static string ValidateRemoteUrl(RepositoryProvider provider, string remoteUrl)
    {
        if (!Uri.TryCreate(remoteUrl?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Repository URL must be an HTTPS URL without credentials or query parameters.",
                nameof(remoteUrl));
        }

        var expectedHost = provider switch
        {
            RepositoryProvider.GitHub => "github.com",
            RepositoryProvider.GitLab => "gitlab.com",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

        if (!string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Repository URL must belong to {expectedHost}.", nameof(remoteUrl));

        if (uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            throw new ArgumentException("Repository URL must contain owner and repository name.", nameof(remoteUrl));

        var normalized = uri.AbsoluteUri.TrimEnd('/');
        if (normalized.Length > RemoteUrlMaxLength)
            throw new ArgumentException($"Repository URL must not exceed {RemoteUrlMaxLength} characters.", nameof(remoteUrl));

        return normalized;
    }

    private static string ValidateBranch(string branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
            throw new ArgumentException("Branch must not be empty.", nameof(branch));

        var normalized = branch.Trim();
        if (normalized.Length > BranchMaxLength ||
            normalized.Contains("..", StringComparison.Ordinal) ||
            normalized.StartsWith('/') ||
            normalized.EndsWith('/') ||
            normalized.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Branch name is invalid.", nameof(branch));
        }

        return normalized;
    }

    private static string ValidateCommit(string commit)
    {
        var normalized = commit?.Trim() ?? string.Empty;
        if (!CommitPattern().IsMatch(normalized))
            throw new ArgumentException("Commit hash is invalid.", nameof(commit));

        return normalized.ToLowerInvariant();
    }
}
