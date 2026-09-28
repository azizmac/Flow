using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

/// <summary>Интеграция с Git-хостингами (docs/TZ_scm_integration.md): подключения, репозитории, привязки, связи, доставки.</summary>
public interface IScmStore
{
    Task<IReadOnlyList<ScmConnection>> GetConnectionsAsync(CancellationToken cancellationToken);

    Task<ScmConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScmRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken);

    Task<ScmRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken);

    Task<ScmRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken);

    /// <summary>Привязки: к проекту (boardId) или репозитория (repositoryId).</summary>
    Task<IReadOnlyList<ScmRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken);

    Task<ScmLink?> FindLinkAsync(Guid taskId, Guid repositoryId, ScmLinkKind kind, string externalId, CancellationToken cancellationToken);

    /// <summary>Ветки репозитория с этим именем — у всех задач (удаление ветки закрывает их связи).</summary>
    Task<IReadOnlyList<ScmLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScmLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Состояние последнего PR каждой задачи — значок в списке. Нет PR — задачи в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, ScmLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken);

    /// <summary>Есть ли в очереди доставка этого события — чтобы не ставить вторую дозагрузку истории поверх первой.</summary>
    Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken);

    /// <summary>Сколько доставок с ошибкой у каждого репозитория — значок в интеграциях. Без ошибок — репозитория в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken);

    Task<ScmDelivery?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Id доставок, которые пора обработать (Pending, NextAttemptAt ≤ now), старые первыми.</summary>
    Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ScmDelivery>> GetDeliveriesAsync(Guid repositoryId, ScmDeliveryStatus? status, int limit, CancellationToken cancellationToken);

    /// <summary>Удалить обработанные доставки старше даты — возвращает число строк.</summary>
    Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken);

    void Add(ScmConnection connection);

    void Add(ScmRepository repository);

    void Add(ScmRepositoryBoard binding);

    void Add(ScmLink link);

    void Add(ScmDelivery delivery);

    void Remove(ScmConnection connection);

    void Remove(ScmRepository repository);

    void Remove(ScmRepositoryBoard binding);
}
