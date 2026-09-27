using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Репозиторий у хостинга — для выбора при подключении.</summary>
public sealed record ScmRemoteRepository(string ExternalId, string FullName, string WebUrl, string? DefaultBranch);

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
}

public sealed class ScmProviderException(string message, Exception? inner = null) : Exception(message, inner);

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
