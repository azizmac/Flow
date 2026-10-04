using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitDevelopmentLinkRepository : IGitDevelopmentLinkRepository
{
    public List<GitDevelopmentLink> Items { get; } = [];

    public Task<GitDevelopmentLink?> FindAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId));

    public Task<IReadOnlyList<GitDevelopmentLink>> GetByBranchAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitDevelopmentLink>>(Items.Where(l => l.RepositoryId == repositoryId && l.Kind == GitDevelopmentLinkKind.Branch && l.ExternalId == branch).ToList());

    public Task<IReadOnlyList<GitDevelopmentLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitDevelopmentLink>>(Items.Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToList());

    public Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>>(Items
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == GitDevelopmentLinkKind.PullRequest && l.State != null)
            .GroupBy(l => l.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First().State!.Value));

    public void Add(GitDevelopmentLink link) => Items.Add(link);
}
