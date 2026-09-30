using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeScmStore : IScmStore
{
    public List<GitHostConnection> Connections { get; } = [];
    public List<GitRepository> Repositories { get; } = [];
    public List<GitRepositoryBoard> Bindings { get; } = [];
    public List<GitDevelopmentLink> Links { get; } = [];
    public List<GitIntegrationJob> Deliveries { get; } = [];

    public Task<IReadOnlyList<GitHostConnection>> GetConnectionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitHostConnection>>(Connections.OrderBy(c => c.Name).ToList());

    public Task<GitHostConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Connections.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<GitRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitRepository>>(Repositories.Where(r => connectionId == null || r.ConnectionId == connectionId).ToList());

    public Task<GitRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.SingleOrDefault(r => r.Id == id));

    public Task<GitRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.SingleOrDefault(r => r.ConnectionId == connectionId && r.ExternalId == externalId));

    public Task<IReadOnlyList<GitRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitRepositoryBoard>>(Bindings.Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId)).ToList());

    public Task<GitDevelopmentLink?> FindLinkAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Links.SingleOrDefault(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId));

    public Task<IReadOnlyList<GitDevelopmentLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitDevelopmentLink>>(Links.Where(l => l.RepositoryId == repositoryId && l.Kind == GitDevelopmentLinkKind.Branch && l.ExternalId == branch).ToList());

    public Task<IReadOnlyList<GitDevelopmentLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitDevelopmentLink>>(Links.Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToList());

    public Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>>(Links
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == GitDevelopmentLinkKind.PullRequest && l.State != null)
            .GroupBy(l => l.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First().State!.Value));

    public Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId));

    public Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == GitIntegrationJobStatus.Pending));

    public Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Deliveries.Where(d => d.Status == GitIntegrationJobStatus.Failed)
            .GroupBy(d => d.RepositoryId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<GitIntegrationJob?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Deliveries.Where(d => d.Status == GitIntegrationJobStatus.Pending && d.NextAttemptAt <= utcNow).Take(limit).Select(d => d.Id).ToList());

    public Task<IReadOnlyList<GitIntegrationJob>> GetDeliveriesAsync(Guid repositoryId, GitIntegrationJobStatus? status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitIntegrationJob>>(Deliveries.Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status)).Take(limit).ToList());

    public Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.RemoveAll(d => d.ReceivedAt < olderThan && d.Status != GitIntegrationJobStatus.Pending));

    public void Add(GitHostConnection connection) => Connections.Add(connection);
    public void Add(GitRepository repository) => Repositories.Add(repository);
    public void Add(GitRepositoryBoard binding) => Bindings.Add(binding);
    public void Add(GitDevelopmentLink link) => Links.Add(link);
    public void Add(GitIntegrationJob delivery) => Deliveries.Add(delivery);
    public void Remove(GitHostConnection connection) => Connections.Remove(connection);
    public void Remove(GitRepository repository) => Repositories.Remove(repository);
    public void Remove(GitRepositoryBoard binding) => Bindings.Remove(binding);
}

/// <summary>Хостинг в памяти: список репозиториев, созданные и удалённые вебхуки, заданный отказ.</summary>
public sealed class FakeScmProviderClient : IScmProviderClient
{
    public List<ScmRemoteRepository> Remote { get; } = [new("101", "acme/web", "https://github.com/acme/web", "main")];
    public List<(string Repository, string Url, string Secret)> CreatedHooks { get; } = [];
    public List<string> DeletedHooks { get; } = [];
    public string? Failure { get; set; }

    public Task<string> CheckAsync(GitHostConnection connection, string token, CancellationToken cancellationToken) =>
        Failure is { } f ? throw new ScmProviderException(f) : Task.FromResult(token == "bad" ? throw new ScmProviderException("Токен не принят.") : "octocat");

    public Task<IReadOnlyList<ScmRemoteRepository>> ListRepositoriesAsync(GitHostConnection connection, string token, string? query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmRemoteRepository>>(Remote.Where(r => query is null || r.FullName.Contains(query)).ToList());

    public Task<ScmRemoteRepository?> GetRepositoryAsync(GitHostConnection connection, string token, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Remote.SingleOrDefault(r => r.ExternalId == externalId));

    public Task<string> CreateWebhookAsync(GitHostConnection connection, string token, GitRepository repository, string url, string secret, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedHooks.Add((repository.FullName, url, secret));
        return Task.FromResult(CreatedHooks.Count.ToString());
    }

    public Task DeleteWebhookAsync(GitHostConnection connection, string token, GitRepository repository, string webhookId, CancellationToken cancellationToken)
    {
        DeletedHooks.Add(webhookId);
        return Task.CompletedTask;
    }

    /// <summary>История для дозагрузки; RateLimitUntil — ответить «лимит исчерпан» один раз.</summary>
    public ScmHistory History { get; set; } = new([], []);
    public DateTime? RateLimitUntil { get; set; }
    public List<(DateTime Since, int MaxPullRequests, int MaxCommits)> HistoryCalls { get; } = [];

    public Task<ScmHistory> GetHistoryAsync(GitHostConnection connection, string token, GitRepository repository, DateTime commitsSince,
        int maxPullRequests, int maxCommits, CancellationToken cancellationToken)
    {
        HistoryCalls.Add((commitsSince, maxPullRequests, maxCommits));
        if (RateLimitUntil is { } until)
        {
            RateLimitUntil = null;
            throw new ScmRateLimitException(until);
        }
        if (Failure is { } f)
            throw new ScmProviderException(f);
        return Task.FromResult(History);
    }

    public List<(string Name, string From)> CreatedBranches { get; } = [];
    public List<(string Source, string Target, string Title, string Body, bool Draft)> CreatedPullRequests { get; } = [];
    public List<(string Number, string Body)> Comments { get; } = [];

    public Task CreateBranchAsync(GitHostConnection connection, string token, GitRepository repository, string name, string fromBranch, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedBranches.Add((name, fromBranch));
        return Task.CompletedTask;
    }

    public Task<Flow.Application.Features.Scm.ScmPullRequest> CreatePullRequestAsync(GitHostConnection connection, string token, GitRepository repository,
        string sourceBranch, string targetBranch, string title, string body, bool draft, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedPullRequests.Add((sourceBranch, targetBranch, title, body, draft));
        var number = (41 + CreatedPullRequests.Count).ToString();
        return Task.FromResult(new Flow.Application.Features.Scm.ScmPullRequest(number, title, body, GitDevelopmentLinkState.Open,
            $"{repository.WebUrl}/pull/{number}", "flow-bot-token", sourceBranch, targetBranch, DateTime.UtcNow));
    }

    public Task CommentOnPullRequestAsync(GitHostConnection connection, string token, GitRepository repository, string number, string body, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        Comments.Add((number, body));
        return Task.CompletedTask;
    }
}

/// <summary>Обратимое «шифрование» для тестов: префикс, а не криптография.</summary>
public sealed class FakeScmSecretProtector : IScmSecretProtector
{
    public string Protect(string secret) => "p:" + secret;

    public string? TryUnprotect(string protectedSecret) => protectedSecret.StartsWith("p:") ? protectedSecret[2..] : null;
}
