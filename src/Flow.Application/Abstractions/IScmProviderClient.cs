using Flow.Application.Features.Scm;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

/// <summary>Репозиторий у хостинга — для выбора при подключении.</summary>
public sealed record ScmRemoteRepository(string ExternalId, string FullName, string WebUrl, string? DefaultBranch);

/// <summary>История репозитория для дозагрузки (этап 5B): последние PR и коммиты ветки по умолчанию.</summary>
public sealed record ScmHistory(IReadOnlyList<ScmPullRequest> PullRequests, IReadOnlyList<ScmCommit> Commits);

/// <summary>
/// API хостинга (docs/TZ_scm_integration.md §6): проверка токена, список репозиториев, создание и удаление вебхука.
/// Реализации — GitHub, GitLab, Gitea (он же Forgejo) в Flow.Infrastructure/Scm. Сбой — ScmProviderException с
/// понятной человеку причиной (нет прав, неверный токен, хостинг недоступен).
/// </summary>
public interface IScmProviderClient
{
    /// <summary>Логин владельца токена.</summary>
    Task<string> CheckAsync(ScmConnection connection, string token, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScmRemoteRepository>> ListRepositoriesAsync(ScmConnection connection, string token, string? query, CancellationToken cancellationToken);

    Task<ScmRemoteRepository?> GetRepositoryAsync(ScmConnection connection, string token, string externalId, CancellationToken cancellationToken);

    /// <summary>Вебхук на push, PR/MR и ветки с этим адресом и секретом; ответ — id вебхука у хостинга.</summary>
    Task<string> CreateWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string url, string secret, CancellationToken cancellationToken);

    Task DeleteWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string webhookId, CancellationToken cancellationToken);

    /// <summary>
    /// Последние <paramref name="maxPullRequests"/> PR (по дате изменения) и коммиты ветки по умолчанию с даты
    /// <paramref name="commitsSince"/> — не больше <paramref name="maxCommits"/>.
    /// </summary>
    Task<ScmHistory> GetHistoryAsync(ScmConnection connection, string token, ScmRepository repository, DateTime commitsSince,
        int maxPullRequests, int maxCommits, CancellationToken cancellationToken);

    // Действия из Flow (этап 5D): запись на хостинг от имени токена подключения.

    /// <summary>Ветка <paramref name="name"/> от <paramref name="fromBranch"/>; уже есть — ScmProviderException.</summary>
    Task CreateBranchAsync(ScmConnection connection, string token, ScmRepository repository, string name, string fromBranch, CancellationToken cancellationToken);

    /// <summary>PR/MR из <paramref name="sourceBranch"/> в <paramref name="targetBranch"/>; ответ — созданный PR.</summary>
    Task<ScmPullRequest> CreatePullRequestAsync(ScmConnection connection, string token, ScmRepository repository, string sourceBranch, string targetBranch,
        string title, string body, bool draft, CancellationToken cancellationToken);

    /// <summary>Комментарий в PR/MR с номером <paramref name="number"/>.</summary>
    Task CommentOnPullRequestAsync(ScmConnection connection, string token, ScmRepository repository, string number, string body, CancellationToken cancellationToken);
}

/// <summary>
/// «Токен» для вызова: у подключения по токену — он сам, у GitHub App — закрытый ключ PEM, из которого клиент сам
/// получает токен установки (JWT → POST /app/installations/{id}/access_tokens) и держит его до истечения.
/// </summary>
public class ScmProviderException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Хостинг исчерпал лимит запросов — не сбой, а пауза до <see cref="ResetAt"/> (UTC).</summary>
public sealed class ScmRateLimitException(DateTime resetAt) : ScmProviderException($"Хостинг исчерпал лимит запросов; продолжим после {resetAt:HH:mm} UTC.")
{
    public DateTime ResetAt { get; } = resetAt;
}

/// <summary>
/// Шифрование токенов и секретов вебхуков (IDataProtector, purpose «Flow.Scm»). Ключи DataProtection должны жить на
/// диске (DataProtection:KeysPath): с эфемерными ключами после рестарта секреты не расшифруются — тогда
/// <see cref="TryUnprotect"/> вернёт null, и подключение попросит переподключить.
/// </summary>
public interface IScmSecretProtector
{
    string Protect(string secret);

    string? TryUnprotect(string protectedSecret);
}
