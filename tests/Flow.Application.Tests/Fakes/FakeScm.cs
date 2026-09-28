using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeScmStore : IScmStore
{
    public List<ScmConnection> Connections { get; } = [];
    public List<ScmRepository> Repositories { get; } = [];
    public List<ScmRepositoryBoard> Bindings { get; } = [];
    public List<ScmLink> Links { get; } = [];
    public List<ScmDelivery> Deliveries { get; } = [];

    public Task<IReadOnlyList<ScmConnection>> GetConnectionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmConnection>>(Connections.OrderBy(c => c.Name).ToList());

    public Task<ScmConnection?> GetConnectionAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Connections.SingleOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<ScmRepository>> GetRepositoriesAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmRepository>>(Repositories.Where(r => connectionId == null || r.ConnectionId == connectionId).ToList());

    public Task<ScmRepository?> GetRepositoryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.SingleOrDefault(r => r.Id == id));

    public Task<ScmRepository?> FindRepositoryAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Repositories.SingleOrDefault(r => r.ConnectionId == connectionId && r.ExternalId == externalId));

    public Task<IReadOnlyList<ScmRepositoryBoard>> GetBindingsAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmRepositoryBoard>>(Bindings.Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId)).ToList());

    public Task<ScmLink?> FindLinkAsync(Guid taskId, Guid repositoryId, ScmLinkKind kind, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Links.SingleOrDefault(l => l.TaskId == taskId && l.RepositoryId == repositoryId && l.Kind == kind && l.ExternalId == externalId));

    public Task<IReadOnlyList<ScmLink>> GetBranchLinksAsync(Guid repositoryId, string branch, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmLink>>(Links.Where(l => l.RepositoryId == repositoryId && l.Kind == ScmLinkKind.Branch && l.ExternalId == branch).ToList());

    public Task<IReadOnlyList<ScmLink>> GetLinksByTaskAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmLink>>(Links.Where(l => l.TaskId == taskId).OrderByDescending(l => l.OccurredAt).ToList());

    public Task<IReadOnlyDictionary<Guid, ScmLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, ScmLinkState>>(Links
            .Where(l => taskIds.Contains(l.TaskId) && l.Kind == ScmLinkKind.PullRequest && l.State != null)
            .GroupBy(l => l.TaskId).ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.UpdatedAt).First().State!.Value));

    public Task<bool> DeliveryExistsAsync(Guid repositoryId, string deliveryId, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(d => d.RepositoryId == repositoryId && d.DeliveryId == deliveryId));

    public Task<bool> HasPendingDeliveryAsync(Guid repositoryId, string eventName, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.Any(d => d.RepositoryId == repositoryId && d.Event == eventName && d.Status == ScmDeliveryStatus.Pending));

    public Task<IReadOnlyDictionary<Guid, int>> GetFailedDeliveryCountsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(Deliveries.Where(d => d.Status == ScmDeliveryStatus.Failed)
            .GroupBy(d => d.RepositoryId).ToDictionary(g => g.Key, g => g.Count()));

    public Task<ScmDelivery?> GetDeliveryAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Guid>> GetDueDeliveryIdsAsync(DateTime utcNow, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Deliveries.Where(d => d.Status == ScmDeliveryStatus.Pending && d.NextAttemptAt <= utcNow).Take(limit).Select(d => d.Id).ToList());

    public Task<IReadOnlyList<ScmDelivery>> GetDeliveriesAsync(Guid repositoryId, ScmDeliveryStatus? status, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmDelivery>>(Deliveries.Where(d => d.RepositoryId == repositoryId && (status == null || d.Status == status)).Take(limit).ToList());

    public Task<int> PurgeDeliveriesAsync(DateTime olderThan, CancellationToken cancellationToken) =>
        Task.FromResult(Deliveries.RemoveAll(d => d.ReceivedAt < olderThan && d.Status != ScmDeliveryStatus.Pending));

    public void Add(ScmConnection connection) => Connections.Add(connection);
    public void Add(ScmRepository repository) => Repositories.Add(repository);
    public void Add(ScmRepositoryBoard binding) => Bindings.Add(binding);
    public void Add(ScmLink link) => Links.Add(link);
    public void Add(ScmDelivery delivery) => Deliveries.Add(delivery);
    public void Remove(ScmConnection connection) => Connections.Remove(connection);
    public void Remove(ScmRepository repository) => Repositories.Remove(repository);
    public void Remove(ScmRepositoryBoard binding) => Bindings.Remove(binding);
}

/// <summary>Хостинг в памяти: список репозиториев, созданные и удалённые вебхуки, заданный отказ.</summary>
public sealed class FakeScmProviderClient : IScmProviderClient
{
    public List<ScmRemoteRepository> Remote { get; } = [new("101", "acme/web", "https://github.com/acme/web", "main")];
    public List<(string Repository, string Url, string Secret)> CreatedHooks { get; } = [];
    public List<string> DeletedHooks { get; } = [];
    public string? Failure { get; set; }

    public Task<string> CheckAsync(ScmConnection connection, string token, CancellationToken cancellationToken) =>
        Failure is { } f ? throw new ScmProviderException(f) : Task.FromResult(token == "bad" ? throw new ScmProviderException("Токен не принят.") : "octocat");

    public Task<IReadOnlyList<ScmRemoteRepository>> ListRepositoriesAsync(ScmConnection connection, string token, string? query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScmRemoteRepository>>(Remote.Where(r => query is null || r.FullName.Contains(query)).ToList());

    public Task<ScmRemoteRepository?> GetRepositoryAsync(ScmConnection connection, string token, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Remote.SingleOrDefault(r => r.ExternalId == externalId));

    public Task<string> CreateWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string url, string secret, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedHooks.Add((repository.FullName, url, secret));
        return Task.FromResult(CreatedHooks.Count.ToString());
    }

    public Task DeleteWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string webhookId, CancellationToken cancellationToken)
    {
        DeletedHooks.Add(webhookId);
        return Task.CompletedTask;
    }

    /// <summary>История для дозагрузки; RateLimitUntil — ответить «лимит исчерпан» один раз.</summary>
    public ScmHistory History { get; set; } = new([], []);
    public DateTime? RateLimitUntil { get; set; }
    public List<(DateTime Since, int MaxPullRequests, int MaxCommits)> HistoryCalls { get; } = [];

    public Task<ScmHistory> GetHistoryAsync(ScmConnection connection, string token, ScmRepository repository, DateTime commitsSince,
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

    public Task CreateBranchAsync(ScmConnection connection, string token, ScmRepository repository, string name, string fromBranch, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedBranches.Add((name, fromBranch));
        return Task.CompletedTask;
    }

    public Task<Flow.Application.Features.Scm.ScmPullRequest> CreatePullRequestAsync(ScmConnection connection, string token, ScmRepository repository,
        string sourceBranch, string targetBranch, string title, string body, bool draft, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new ScmProviderException(f);
        CreatedPullRequests.Add((sourceBranch, targetBranch, title, body, draft));
        var number = (41 + CreatedPullRequests.Count).ToString();
        return Task.FromResult(new Flow.Application.Features.Scm.ScmPullRequest(number, title, body, ScmLinkState.Open,
            $"{repository.WebUrl}/pull/{number}", "flow-bot-token", sourceBranch, targetBranch, DateTime.UtcNow));
    }

    public Task CommentOnPullRequestAsync(ScmConnection connection, string token, ScmRepository repository, string number, string body, CancellationToken cancellationToken)
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
