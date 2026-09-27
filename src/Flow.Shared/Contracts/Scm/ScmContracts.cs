namespace Flow.Shared.Contracts.Scm;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A). Зеркала доменных enum'ов — Shared не ссылается на Domain.

public enum ScmProvider { GitHub = 0, GitLab = 1, Gitea = 2, Forgejo = 3 }

public enum ScmLinkKind { Branch = 0, Commit = 1, PullRequest = 2 }

public enum ScmLinkState { Open = 0, Draft = 1, Merged = 2, Closed = 3 }

public enum ScmDeliveryStatus { Pending = 0, Done = 1, Failed = 2, Ignored = 3 }

/// <summary>Подключение; токен наружу не отдаётся никогда. NeedsReconnect — токен не расшифровывается (сменились ключи).</summary>
public sealed record ScmConnectionResponse(
    Guid Id, ScmProvider Provider, string Name, string? BaseUrl, DateTime CreatedAt, DateTime? LastCheckAt,
    string? CheckedLogin, string? LastError, bool NeedsReconnect, IReadOnlyList<ScmRepositoryResponse> Repositories);

public sealed record CreateScmConnectionRequest(ScmProvider Provider, string Name, string Token, string? BaseUrl = null);

/// <summary>Token = null — не менять.</summary>
public sealed record UpdateScmConnectionRequest(string Name, string? BaseUrl = null, string? Token = null);

public sealed record ScmRemoteRepositoryResponse(string ExternalId, string FullName, string WebUrl, string? DefaultBranch, bool IsAdded);

public sealed record AddScmRepositoryRequest(Guid ConnectionId, string ExternalId);

/// <summary>
/// Репозиторий. WebhookCreated = false — хостинг не дал создать вебхук (нет прав или не задан Scm:PublicBaseUrl):
/// тогда ManualWebhookUrl/ManualWebhookSecret приходят один раз в ответе на добавление, для ручной настройки.
/// </summary>
public sealed record ScmRepositoryResponse(
    Guid Id, Guid ConnectionId, string FullName, string WebUrl, string DefaultBranch, bool IsActive, bool WebhookCreated,
    DateTime? LastDeliveryAt, IReadOnlyList<Guid> BoardIds, string? ManualWebhookUrl = null, string? ManualWebhookSecret = null,
    string? WebhookError = null);

/// <summary>Репозиторий для вкладки «Разработка» проекта: привязан или можно привязать.</summary>
public sealed record ScmBoardRepositoryResponse(Guid RepositoryId, ScmProvider Provider, string FullName, string WebUrl, bool IsBound);

public sealed record ScmLinkResponse(
    Guid Id, Guid RepositoryId, string RepositoryName, ScmProvider Provider, ScmLinkKind Kind, string ExternalId, string Url,
    string Title, ScmLinkState? State, string? AuthorLogin, Guid? AuthorUserId, string? SourceBranch, string? TargetBranch,
    DateTime OccurredAt);

/// <summary>
/// Блок «Разработка» задачи: ветки, PR, последние коммиты и сколько их всего. HasRepositories — проект привязан хотя
/// бы к одному репозиторию: без этого блок в карточке не нужен вовсе.
/// </summary>
public sealed record TaskDevelopmentResponse(
    IReadOnlyList<ScmLinkResponse> Branches, IReadOnlyList<ScmLinkResponse> PullRequests, IReadOnlyList<ScmLinkResponse> Commits, int CommitCount,
    bool HasRepositories);

public sealed record ScmDeliveryResponse(Guid Id, string DeliveryId, string Event, DateTime ReceivedAt, ScmDeliveryStatus Status, int Attempts, string? LastError);
