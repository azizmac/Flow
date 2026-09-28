using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class ScmStore(FlowDbContext db) : IScmStore
{
    public async Task<IReadOnlyList<ScmConnection>> GetConnectionsAsync(CancellationToken cancellationToken) =>
        await db.ScmConnections.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<ScmConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScmRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        await db.ScmRepositories.Where(r => connectionId == null || r.ConnectionId == connectionId).OrderBy(r => r.FullName).ToListAsync(cancellationToken);

    public Task<ScmRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<ScmRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        db.ScmRepositories.FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<ScmRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        await db.ScmRepositoryBoards
            .Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId))
            .ToListAsync(cancellationToken);

    public Task<ScmLink?> FindLinkAsync(Guid taskId, Guid repositoryId, ScmLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        db.ScmLinks.FirstOrDefaultAsync(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<ScmLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        await db.ScmLinks.Where(l => l.RepositoryId == repositoryId && l.Kind == ScmLinkKind.Branch && l.ExternalId == branch).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScmLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.ScmLinks.AsNoTracking().Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, ScmLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, ScmLinkState>();

        var rows = await db.ScmLinks
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == ScmLinkKind.PullRequest && l.State != null)
            .Select(l => new { l.TaskId, l.State, l.UpdatedAt })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.UpdatedAt).First().State!.Value);
    }

    public Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId, cancellationToken);

    public Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == ScmDeliveryStatus.Pending, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == ScmDeliveryStatus.Failed)
            .GroupBy(d => d.RepositoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.Key, r => r.Count, cancellationToken);

    public Task<ScmDelivery?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmDeliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == ScmDeliveryStatus.Pending && d.NextAttemptAt <= utcNow)
            .OrderBy(d => d.ReceivedAt).Take(limit).Select(d => d.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScmDelivery>> GetDeliveriesAsync(Guid repositoryId, ScmDeliveryStatus? status, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.AsNoTracking()
            .Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status))
            .OrderByDescending(d => d.ReceivedAt).Take(limit).ToListAsync(cancellationToken);

    public Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        db.ScmDeliveries.Where(d => d.ReceivedAt < olderThan && d.Status != ScmDeliveryStatus.Pending).ExecuteDeleteAsync(cancellationToken);

    public void Add(ScmConnection connection) => db.ScmConnections.Add(connection);

    public void Add(ScmRepository repository) => db.ScmRepositories.Add(repository);

    public void Add(ScmRepositoryBoard binding) => db.ScmRepositoryBoards.Add(binding);

    public void Add(ScmLink link) => db.ScmLinks.Add(link);

    public void Add(ScmDelivery delivery) => db.ScmDeliveries.Add(delivery);

    public void Remove(ScmConnection connection) => db.ScmConnections.Remove(connection);

    public void Remove(ScmRepository repository) => db.ScmRepositories.Remove(repository);

    public void Remove(ScmRepositoryBoard binding) => db.ScmRepositoryBoards.Remove(binding);
}
