using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitIntegrationJobRepository : IGitIntegrationJobRepository
{
    public List<GitIntegrationJob> Items { get; } = [];

    public Task<bool> ExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId));

    public Task<bool> HasPendingAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == GitIntegrationJobStatus.Pending));

    public Task<IReadOnlyDictionary<Guid, int>> GetFailedCountsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Items.Where(d => d.Status == GitIntegrationJobStatus.Failed)
            .GroupBy(d => d.RepositoryId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<GitIntegrationJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Items.Where(d => d.Status == GitIntegrationJobStatus.Pending && d.NextAttemptAt <= utcNow).Take(limit).Select(d => d.Id).ToList());

    public Task<IReadOnlyList<GitIntegrationJob>> GetByRepositoryIdAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitIntegrationJob>>(Items.Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status)).Take(limit).ToList());

    public Task<int> PurgeProcessedAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        Task.FromResult(Items.RemoveAll(d => d.ReceivedAt < olderThan && d.Status != GitIntegrationJobStatus.Pending));

    public void Add(GitIntegrationJob delivery) => Items.Add(delivery);
}
