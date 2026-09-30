using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IGitIntegrationJobRepository
{
    Task<bool> ExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken);

    /// <summary>Есть ли в очереди доставка этого события — чтобы не ставить вторую дозагрузку истории поверх первой.</summary>
    Task<bool> HasPendingAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken);

    /// <summary>Сколько доставок с ошибкой у каждого репозитория — значок в интеграциях. Без ошибок — репозитория в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, int>> GetFailedCountsAsync(CancellationToken cancellationToken);

    Task<GitIntegrationJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Id доставок, которые пора обработать (Pending, NextAttemptAt ≤ now), старые первыми.</summary>
    Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitIntegrationJob>> GetByRepositoryIdAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken);

    /// <summary>Удалить обработанные доставки старше даты — возвращает число строк.</summary>
    Task<int> PurgeProcessedAsync(DateTime olderThan, CancellationToken cancellationToken);

    void Add(GitIntegrationJob delivery);
}
