using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class ScmStore(FlowDbContext db) : IScmStore
{
    public async Task<IReadOnlyList<GitHostConnection>> GetConnectionsAsync(CancellationToken cancellationToken) =>
        await db.ScmConnections.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<GitHostConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        await db.ScmRepositories.Where(r => connectionId == null || r.ConnectionId == connectionId).OrderBy(r => r.FullName).ToListAsync(cancellationToken);

    public Task<GitRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<GitRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        db.ScmRepositories.FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<GitRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        await db.ScmRepositoryBoards
            .Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId))
            .ToListAsync(cancellationToken);

    public Task<GitDevelopmentLink?> FindLinkAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        db.ScmLinks.FirstOrDefaultAsync(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<GitDevelopmentLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        await db.ScmLinks.Where(l => l.RepositoryId == repositoryId && l.Kind == GitDevelopmentLinkKind.Branch && l.ExternalId == branch).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GitDevelopmentLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.ScmLinks.AsNoTracking().Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, GitDevelopmentLinkState>();

        var rows = await db.ScmLinks
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == GitDevelopmentLinkKind.PullRequest && l.State != null)
            .Select(l => new { l.TaskId, l.State, l.UpdatedAt })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.UpdatedAt).First().State!.Value);
    }

    public Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId, cancellationToken);

    public Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        db.ScmDeliveries.AnyAsync(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == GitIntegrationJobStatus.Pending, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == GitIntegrationJobStatus.Failed)
            .GroupBy(d => d.RepositoryId).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(r => r.Key, r => r.Count, cancellationToken);

    public Task<GitIntegrationJob?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmDeliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.Where(d => d.Status == GitIntegrationJobStatus.Pending && d.NextAttemptAt <= utcNow)
            .OrderBy(d => d.ReceivedAt).Take(limit).Select(d => d.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GitIntegrationJob>> GetDeliveriesAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken) =>
        await db.ScmDeliveries.AsNoTracking()
            .Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status))
            .OrderByDescending(d => d.ReceivedAt).Take(limit).ToListAsync(cancellationToken);

    public Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        db.ScmDeliveries.Where(d => d.ReceivedAt < olderThan && d.Status != GitIntegrationJobStatus.Pending).ExecuteDeleteAsync(cancellationToken);

    public void Add(GitHostConnection connection) => db.ScmConnections.Add(connection);

    public void Add(GitRepository repository) => db.ScmRepositories.Add(repository);

    public void Add(GitRepositoryBoard binding) => db.ScmRepositoryBoards.Add(binding);

    public void Add(GitDevelopmentLink link) => db.ScmLinks.Add(link);

    public void Add(GitIntegrationJob delivery) => db.ScmDeliveries.Add(delivery);

    public void Remove(GitHostConnection connection) => db.ScmConnections.Remove(connection);

    public void Remove(GitRepository repository) => db.ScmRepositories.Remove(repository);

    public void Remove(GitRepositoryBoard binding) => db.ScmRepositoryBoards.Remove(binding);
}
