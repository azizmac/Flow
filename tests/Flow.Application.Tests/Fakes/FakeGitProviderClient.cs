using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitProviderClient : IGitProviderClient
{
    public List<GitRemoteRepository> Remote { get; } = [new("101", "acme/web", "https://github.com/acme/web", "main")];
    public List<(string Repository, string Url, string Secret)> CreatedHooks { get; } = [];
    public List<string> DeletedHooks { get; } = [];
    public string? Failure { get; set; }

    public Task<string> CheckAsync(GitHostConnection connection, string token, CancellationToken cancellationToken) =>
        Failure is { } f ? throw new GitProviderException(f) : Task.FromResult(token == "bad" ? throw new GitProviderException("Токен не принят.") : "octocat");

    public Task<IReadOnlyList<GitRemoteRepository>> ListRepositoriesAsync(GitHostConnection connection, string token, string? query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitRemoteRepository>>(Remote.Where(r => query is null || r.FullName.Contains(query)).ToList());

    public Task<GitRemoteRepository?> GetRepositoryAsync(GitHostConnection connection, string token, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Remote.SingleOrDefault(r => r.ExternalId == externalId));

    public Task<string> CreateWebhookAsync(GitHostConnection connection, string token, GitRepository repository, string url, string secret, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new GitProviderException(f);
        CreatedHooks.Add((repository.FullName, url, secret));
        return Task.FromResult(CreatedHooks.Count.ToString());
    }

    public Task DeleteWebhookAsync(GitHostConnection connection, string token, GitRepository repository, string webhookId, CancellationToken cancellationToken)
    {
        DeletedHooks.Add(webhookId);
        return Task.CompletedTask;
    }

    /// <summary>История для дозагрузки; RateLimitUntil — ответить «лимит исчерпан» один раз.</summary>
    public GitHistory History { get; set; } = new([], []);
    public DateTime? RateLimitUntil { get; set; }
    public List<(DateTime Since, int MaxPullRequests, int MaxCommits)> HistoryCalls { get; } = [];

    public Task<GitHistory> GetHistoryAsync(GitHostConnection connection, string token, GitRepository repository, DateTime commitsSince,
        int maxPullRequests, int maxCommits, CancellationToken cancellationToken)
    {
        HistoryCalls.Add((commitsSince, maxPullRequests, maxCommits));
        if (RateLimitUntil is { } until)
        {
            RateLimitUntil = null;
            throw new GitRateLimitException(until);
        }
        if (Failure is { } f)
            throw new GitProviderException(f);
        return Task.FromResult(History);
    }

    public List<(string Name, string From)> CreatedBranches { get; } = [];
    public List<(string Source, string Target, string Title, string Body, bool Draft)> CreatedPullRequests { get; } = [];
    public List<(string Number, string Body)> Comments { get; } = [];

    public Task CreateBranchAsync(GitHostConnection connection, string token, GitRepository repository, string name, string fromBranch, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new GitProviderException(f);
        CreatedBranches.Add((name, fromBranch));
        return Task.CompletedTask;
    }

    public Task<Flow.Application.Features.GitIntegration.GitPullRequest> CreatePullRequestAsync(GitHostConnection connection, string token, GitRepository repository,
        string sourceBranch, string targetBranch, string title, string body, bool draft, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new GitProviderException(f);
        CreatedPullRequests.Add((sourceBranch, targetBranch, title, body, draft));
        var number = (41 + CreatedPullRequests.Count).ToString();
        return Task.FromResult(new Flow.Application.Features.GitIntegration.GitPullRequest(number, title, body, GitDevelopmentLinkState.Open,
            $"{repository.WebUrl}/pull/{number}", "flow-bot-token", sourceBranch, targetBranch, DateTime.UtcNow));
    }

    public Task CommentOnPullRequestAsync(GitHostConnection connection, string token, GitRepository repository, string number, string body, CancellationToken cancellationToken)
    {
        if (Failure is { } f)
            throw new GitProviderException(f);
        Comments.Add((number, body));
        return Task.CompletedTask;
    }
}
