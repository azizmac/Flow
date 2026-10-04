using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GitDevelopmentLinkRepository(FlowDbContext db) : IGitDevelopmentLinkRepository
{
    public Task<GitDevelopmentLink?> FindAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        db.GitDevelopmentLinks.FirstOrDefaultAsync(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId, cancellationToken);

    public async Task<IReadOnlyList<GitDevelopmentLink>> GetByBranchAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        await db.GitDevelopmentLinks.Where(l => l.RepositoryId == repositoryId && l.Kind == GitDevelopmentLinkKind.Branch && l.ExternalId == branch).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GitDevelopmentLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.GitDevelopmentLinks.AsNoTracking().Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, GitDevelopmentLinkState>();

        var rows = await db.GitDevelopmentLinks
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == GitDevelopmentLinkKind.PullRequest && l.State != null)
            .Select(l => new { l.TaskId, l.State, l.UpdatedAt })
            .ToListAsync(cancellationToken);
        return rows.GroupBy(r => r.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.UpdatedAt).First().State!.Value);
    }

    public void Add(GitDevelopmentLink link) => db.GitDevelopmentLinks.Add(link);
}
