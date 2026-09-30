namespace Flow.Shared.Contracts.GitIntegration;

// Интеграция с Git-хостингами (docs/TZ_git_integration.md, этап 5A). Зеркала доменных enum'ов — Shared не ссылается на Domain.

public enum GitProvider { GitHub = 0, GitLab = 1, Gitea = 2, Forgejo = 3 }

/// <summary>Вход в API: токен или GitHub App (закрытый ключ + Id приложения и установки).</summary>
public enum GitAuthenticationKind { Token = 0, GitHubApp = 1 }

public enum GitDevelopmentLinkKind { Branch = 0, Commit = 1, PullRequest = 2 }

public enum GitDevelopmentLinkState { Open = 0, Draft = 1, Merged = 2, Closed = 3 }

public enum GitIntegrationJobStatus { Pending = 0, Done = 1, Failed = 2, Ignored = 3 }

/// <summary>
/// Подключение; токен и закрытый ключ наружу не отдаются никогда. NeedsReconnect — секрет не расшифровывается
/// (сменились ключи). AppId/InstallationId — только у GitHub App.
/// </summary>
public sealed record GitHostConnectionResponse(
    Guid Id, GitProvider Provider, string Name, string? BaseUrl, DateTime CreatedAt, DateTime? LastCheckAt,
    string? CheckedLogin, string? LastError, bool NeedsReconnect, IReadOnlyList<GitRepositoryResponse> Repositories,
    GitAuthenticationKind AuthKind = GitAuthenticationKind.Token, long? AppId = null, long? InstallationId = null);

/// <summary>Token — токен доступа, а у GitHub App — закрытый ключ приложения (PEM).</summary>
public sealed record CreateGitHostConnectionRequest(GitProvider Provider, string Name, string Token, string? BaseUrl = null,
    GitAuthenticationKind AuthKind = GitAuthenticationKind.Token, long? AppId = null, long? InstallationId = null);

/// <summary>Token = null — не менять; AppId/InstallationId — только у GitHub App, null — не менять.</summary>
public sealed record UpdateGitHostConnectionRequest(string Name, string? BaseUrl = null, string? Token = null, long? AppId = null, long? InstallationId = null);

public sealed record GitRemoteRepositoryResponse(string ExternalId, string FullName, string WebUrl, string? DefaultBranch, bool IsAdded);

public sealed record AddGitRepositoryRequest(Guid ConnectionId, string ExternalId);

/// <summary>
/// Репозиторий. WebhookCreated = false — хостинг не дал создать вебхук (нет прав или не задан Scm:PublicBaseUrl):
/// тогда ManualWebhookUrl/ManualWebhookSecret приходят один раз в ответе на добавление, для ручной настройки.
/// </summary>
public sealed record GitRepositoryResponse(
    Guid Id, Guid ConnectionId, string FullName, string WebUrl, string DefaultBranch, bool IsActive, bool WebhookCreated,
    DateTime? LastDeliveryAt, IReadOnlyList<Guid> BoardIds, string? ManualWebhookUrl = null, string? ManualWebhookSecret = null,
    string? WebhookError = null, int FailedDeliveries = 0);

/// <summary>
/// Репозиторий для вкладки «Разработка» проекта: привязан или можно привязать; у привязанного — автоматизация
/// (этап 5C): статус при открытии PR, статус при влитии в ветку по умолчанию, смарт-коммиты.
/// </summary>
public sealed record GitBoardRepositoryResponse(Guid RepositoryId, GitProvider Provider, string FullName, string WebUrl, bool IsBound,
    Guid? OnPullRequestOpenedStatusId = null, Guid? OnPullRequestMergedStatusId = null, bool SmartCommits = false, bool CommentOnPullRequests = false,
    string DefaultBranch = "main", string SyncState = "Pending", string? LastSyncedCommit = null, DateTime? LastSyncedAt = null,
    string? LastSyncError = null);

/// <summary>
/// Тело PUT /boards/{id}/repositories/{repoId}: привязать и настроить. Статусы — проекта; null — автопереход выключен.
/// CommentOnPullRequests (этап 5D) — комментарий «задача Flow» в каждом новом PR, связанном с задачей проекта.
/// </summary>
public sealed record UpdateGitBindingRequest(Guid? OnPullRequestOpenedStatusId = null, Guid? OnPullRequestMergedStatusId = null, bool SmartCommits = false,
    bool CommentOnPullRequests = false);

public sealed record GitDevelopmentLinkResponse(
    Guid Id, Guid RepositoryId, string RepositoryName, GitProvider Provider, GitDevelopmentLinkKind Kind, string ExternalId, string Url,
    string Title, GitDevelopmentLinkState? State, string? AuthorLogin, Guid? AuthorUserId, string? SourceBranch, string? TargetBranch,
    DateTime OccurredAt, string? Note = null);

/// <summary>
/// Блок «Разработка» задачи: ветки, PR, последние коммиты и сколько их всего. HasRepositories — проект привязан хотя
/// бы к одному репозиторию: без этого блок в карточке не нужен вовсе. Repositories и CanWrite (этап 5D) — куда из
/// карточки можно создать ветку или PR и есть ли на это право.
/// </summary>
public sealed record TaskDevelopmentResponse(
    IReadOnlyList<GitDevelopmentLinkResponse> Branches, IReadOnlyList<GitDevelopmentLinkResponse> PullRequests, IReadOnlyList<GitDevelopmentLinkResponse> Commits, int CommitCount,
    bool HasRepositories, IReadOnlyList<GitTaskRepositoryResponse>? Repositories = null, bool CanWrite = false);

/// <summary>Привязанный к проекту задачи активный репозиторий — цель «Создать ветку / PR».</summary>
public sealed record GitTaskRepositoryResponse(Guid Id, string FullName, string DefaultBranch, GitProvider Provider);

/// <summary>POST /tasks/{id}/development/branch (этап 5D): имя — «КОД-название»; от ветки по умолчанию, если From нет.</summary>
public sealed record CreateGitBranchRequest(Guid RepositoryId, string Name, string? FromBranch = null);

/// <summary>POST /tasks/{id}/development/pull-request: в ветку по умолчанию, если Target нет; заголовок — «КОД Название».</summary>
public sealed record CreateGitPullRequestRequest(Guid RepositoryId, string SourceBranch, string? TargetBranch = null, string? Title = null, bool Draft = false);

/// <summary>
/// Доставка вебхука или задание дозагрузки истории (IsBackfill). NextAttemptAt — когда воркер возьмёт её снова (у
/// Pending после сбоя или паузы из-за лимита запросов хостинга).
/// </summary>
public sealed record GitIntegrationJobResponse(Guid Id, string DeliveryId, string Event, DateTime ReceivedAt, GitIntegrationJobStatus Status, int Attempts, string? LastError,
    DateTime? NextAttemptAt = null, bool IsBackfill = false);
