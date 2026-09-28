namespace Flow.Shared.Contracts.Scm;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A). Зеркала доменных enum'ов — Shared не ссылается на Domain.

public enum ScmProvider { GitHub = 0, GitLab = 1, Gitea = 2, Forgejo = 3 }

/// <summary>Вход в API: токен или GitHub App (закрытый ключ + Id приложения и установки).</summary>
public enum ScmAuthKind { Token = 0, GitHubApp = 1 }

public enum ScmLinkKind { Branch = 0, Commit = 1, PullRequest = 2 }

public enum ScmLinkState { Open = 0, Draft = 1, Merged = 2, Closed = 3 }

public enum ScmDeliveryStatus { Pending = 0, Done = 1, Failed = 2, Ignored = 3 }

/// <summary>
/// Подключение; токен и закрытый ключ наружу не отдаются никогда. NeedsReconnect — секрет не расшифровывается
/// (сменились ключи). AppId/InstallationId — только у GitHub App.
/// </summary>
public sealed record ScmConnectionResponse(
    Guid Id, ScmProvider Provider, string Name, string? BaseUrl, DateTime CreatedAt, DateTime? LastCheckAt,
    string? CheckedLogin, string? LastError, bool NeedsReconnect, IReadOnlyList<ScmRepositoryResponse> Repositories,
    ScmAuthKind AuthKind = ScmAuthKind.Token, long? AppId = null, long? InstallationId = null);

/// <summary>Token — токен доступа, а у GitHub App — закрытый ключ приложения (PEM).</summary>
public sealed record CreateScmConnectionRequest(ScmProvider Provider, string Name, string Token, string? BaseUrl = null,
    ScmAuthKind AuthKind = ScmAuthKind.Token, long? AppId = null, long? InstallationId = null);

/// <summary>Token = null — не менять; AppId/InstallationId — только у GitHub App, null — не менять.</summary>
public sealed record UpdateScmConnectionRequest(string Name, string? BaseUrl = null, string? Token = null, long? AppId = null, long? InstallationId = null);

public sealed record ScmRemoteRepositoryResponse(string ExternalId, string FullName, string WebUrl, string? DefaultBranch, bool IsAdded);

public sealed record AddScmRepositoryRequest(Guid ConnectionId, string ExternalId);

/// <summary>
/// Репозиторий. WebhookCreated = false — хостинг не дал создать вебхук (нет прав или не задан Scm:PublicBaseUrl):
/// тогда ManualWebhookUrl/ManualWebhookSecret приходят один раз в ответе на добавление, для ручной настройки.
/// </summary>
public sealed record ScmRepositoryResponse(
    Guid Id, Guid ConnectionId, string FullName, string WebUrl, string DefaultBranch, bool IsActive, bool WebhookCreated,
    DateTime? LastDeliveryAt, IReadOnlyList<Guid> BoardIds, string? ManualWebhookUrl = null, string? ManualWebhookSecret = null,
    string? WebhookError = null, int FailedDeliveries = 0);

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

/// <summary>
/// Доставка вебхука или задание дозагрузки истории (IsBackfill). NextAttemptAt — когда воркер возьмёт её снова (у
/// Pending после сбоя или паузы из-за лимита запросов хостинга).
/// </summary>
public sealed record ScmDeliveryResponse(Guid Id, string DeliveryId, string Event, DateTime ReceivedAt, ScmDeliveryStatus Status, int Attempts, string? LastError,
    DateTime? NextAttemptAt = null, bool IsBackfill = false);
