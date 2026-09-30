using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GitIntegrationJobRepository(FlowDbContext db) : IGitIntegrationJobRepository
{
    public Task<bool> ExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId, cancellationToken);

    public Task<bool> HasPendingAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == GitIntegrationJobStatus.Pending, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetFailedCountsAsync(CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == GitIntegrationJobStatus.Failed)
            .GroupBy(d => d.RepositoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.Key, r => r.Count, cancellationToken);

    public Task<GitIntegrationJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmDeliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == GitIntegrationJobStatus.Pending && d.NextAttemptAt <= utcNow)
            .OrderBy(d => d.ReceivedAt).Take(limit).Select(d => d.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GitIntegrationJob>> GetByRepositoryIdAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.AsNoTracking()
            .Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status))
            .OrderByDescending(d => d.ReceivedAt).Take(limit).ToListAsync(cancellationToken);

    public Task<int> PurgeProcessedAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        db.ScmDeliveries.Where(d => d.ReceivedAt < olderThan && d.Status != GitIntegrationJobStatus.Pending).ExecuteDeleteAsync(cancellationToken);

    public void Add(GitIntegrationJob delivery) => db.ScmDeliveries.Add(delivery);
}
