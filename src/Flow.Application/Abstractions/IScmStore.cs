using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

/// <summary>Интеграция с Git-хостингами (docs/TZ_scm_integration.md): подключения, репозитории, привязки, связи, доставки.</summary>
public interface IScmStore
{
    Task<IReadOnlyList<GitHostConnection>> GetConnectionsAsync(CancellationToken cancellationToken);

    Task<GitHostConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken);

    Task<GitRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken);

    Task<GitRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken);

    /// <summary>Привязки: к проекту (boardId) или репозитория (repositoryId).</summary>
    Task<IReadOnlyList<GitRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken);

    Task<GitDevelopmentLink?> FindLinkAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken);

    /// <summary>Ветки репозитория с этим именем — у всех задач (удаление ветки закрывает их связи).</summary>
    Task<IReadOnlyList<GitDevelopmentLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitDevelopmentLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Состояние последнего PR каждой задачи — значок в списке. Нет PR — задачи в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken);

    /// <summary>Есть ли в очереди доставка этого события — чтобы не ставить вторую дозагрузку истории поверх первой.</summary>
    Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken);

    /// <summary>Сколько доставок с ошибкой у каждого репозитория — значок в интеграциях. Без ошибок — репозитория в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken);

    Task<GitIntegrationJob?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Id доставок, которые пора обработать (Pending, NextAttemptAt ≤ now), старые первыми.</summary>
    Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitIntegrationJob>> GetDeliveriesAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken);

    /// <summary>Удалить обработанные доставки старше даты — возвращает число строк.</summary>
    Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken);

    void Add(GitHostConnection connection);

    void Add(GitRepository repository);

    void Add(GitRepositoryBoard binding);

    void Add(GitDevelopmentLink link);

    void Add(GitIntegrationJob delivery);

    void Remove(GitHostConnection connection);

    void Remove(GitRepository repository);

    void Remove(GitRepositoryBoard binding);
}
