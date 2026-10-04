using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.GitIntegration;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Xunit;
using DomainState = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkState;
using GitDevelopmentLinkKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using SharedProvider = Flow.Shared.Contracts.GitIntegration.GitProvider;
using SharedAuthKind = Flow.Shared.Contracts.GitIntegration.GitAuthenticationKind;
using SharedDeliveryStatus = Flow.Shared.Contracts.GitIntegration.GitIntegrationJobStatus;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Этап 5B (docs/TZ_git_integration.md): дозагрузка истории при привязке (та же очередь доставок, пауза по лимиту
/// запросов без траты попытки), ручной повтор доставки с ошибкой, подключение GitHub App.
/// </summary>
public class GitBackfillTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<(GitTestContext Context, Guid BoardId, Guid RepositoryId)> ConnectAsync()
    {
        var context = TestMediatorFactory.CreateGitContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Сайт", "WEB"), CancellationToken.None)).Response!;
        var connection = await context.Mediator.Send(new GitHostConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "token", null), CancellationToken.None);
        var repository = (await context.Mediator.Send(new GitRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        return (context, board.Id, repository.Id);
    }

    private static async Task RunQueueAsync(GitTestContext context, DateTime? at = null)
    {
        foreach (var id in await context.Mediator.Send(new GitDueIntegrationJobsQuery(at ?? DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await context.Mediator.Send(new GitIntegrationJobProcessCommand(id), CancellationToken.None);
    }

    [Fact]
    public async Task Binding_Queues_One_Backfill_That_Links_Past_Pull_Requests_And_Commits()
    {
        var (context, boardId, repositoryId) = await ConnectAsync();
        var mediator = context.Mediator;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Форма входа", null, null), CancellationToken.None))!;
        var second = (await mediator.Send(new BoardCreateCommand(Owner, "Второй", "OPS"), CancellationToken.None)).Response!;

        await mediator.Send(new GitBindCommand(Owner, boardId, repositoryId, true), CancellationToken.None);
        await mediator.Send(new GitBindCommand(Owner, second.Id, repositoryId, true), CancellationToken.None);
        var backfill = Assert.Single(context.Git.Jobs);
        Assert.True(backfill.IsBackfill);

        context.Client.History = new GitHistory(
            [
                new GitPullRequest("7", "WEB-1 форма", null, DomainState.Merged, "https://github.com/acme/web/pull/7", "octocat", "web-1", "main",
                    new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)),
                new GitPullRequest("8", "Без кода", null, DomainState.Open, "https://github.com/acme/web/pull/8", "octocat", "misc", "main",
                    new DateTime(2026, 9, 11, 0, 0, 0, DateTimeKind.Utc))
            ],
            [new GitCommit("abc", "WEB-1 поправил вход", "https://github.com/acme/web/commit/abc", "owner@example.com", null,
                new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc))]);
        await RunQueueAsync(context);

        Assert.Equal(GitIntegrationJobStatus.Done, backfill.Status);
        var call = Assert.Single(context.Client.HistoryCalls);
        Assert.Equal((100, 1000), (call.MaxPullRequests, call.MaxCommits));
        Assert.Equal(backfill.ReceivedAt.AddDays(-30), call.Since);

        var development = (await mediator.Send(new TaskDevelopmentQuery(Owner, task.Id), CancellationToken.None))!;
        Assert.Equal(("7", Flow.Shared.Contracts.GitIntegration.GitDevelopmentLinkState.Merged), (development.PullRequests.Single().ExternalId, development.PullRequests.Single().State));
        Assert.Equal("abc", development.Commits.Single().ExternalId);
        Assert.Equal(Owner, development.Commits.Single().AuthorUserId);

        // Ручная дозагрузка ставит новое задание; второе поверх стоящего — нет.
        Assert.True(await mediator.Send(new GitBackfillCommand(Owner, repositoryId), CancellationToken.None));
        Assert.True(await mediator.Send(new GitBackfillCommand(Owner, repositoryId), CancellationToken.None));
        Assert.Equal(2, context.Git.Jobs.Count(d => d.IsBackfill));
        Assert.False(await mediator.Send(new GitBackfillCommand(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Rate_Limit_Postpones_Backfill_Without_Spending_An_Attempt()
    {
        var (context, boardId, repositoryId) = await ConnectAsync();
        await context.Mediator.Send(new GitBindCommand(Owner, boardId, repositoryId, true), CancellationToken.None);
        var reset = DateTime.UtcNow.AddMinutes(20);
        context.Client.RateLimitUntil = reset;

        await RunQueueAsync(context);
        var backfill = context.Git.Jobs.Single();
        Assert.Equal((GitIntegrationJobStatus.Pending, 0, reset), (backfill.Status, backfill.Attempts, backfill.NextAttemptAt));
        Assert.Contains("лимит", backfill.LastError);

        // До сброса воркер его не берёт, после — доделывает.
        await RunQueueAsync(context);
        Assert.Single(context.Client.HistoryCalls);
        await RunQueueAsync(context, reset.AddSeconds(1));
        Assert.Equal(GitIntegrationJobStatus.Done, backfill.Status);
    }

    [Fact]
    public async Task Failed_Delivery_Can_Be_Retried_By_Admin_Only()
    {
        var (context, boardId, repositoryId) = await ConnectAsync();
        await context.Mediator.Send(new GitBindCommand(Owner, boardId, repositoryId, true), CancellationToken.None);
        var backfill = context.Git.Jobs.Single();
        for (var i = 0; i < GitIntegrationJob.MaxAttempts; i++)
            await context.Mediator.Send(new GitIntegrationJobFailCommand(backfill.Id, "Хостинг недоступен"), CancellationToken.None);
        Assert.Equal(GitIntegrationJobStatus.Failed, backfill.Status);

        var connections = await context.Mediator.Send(new GitHostConnectionListQuery(Owner), CancellationToken.None);
        Assert.Equal(1, connections.Single().Repositories.Single().FailedDeliveries);
        var listed = (await context.Mediator.Send(new GitIntegrationJobsQuery(Owner, repositoryId, SharedDeliveryStatus.Failed), CancellationToken.None))!;
        Assert.True(listed.Single().IsBackfill);

        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        context.Users.Add(member);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Mediator.Send(new GitIntegrationJobRetryCommand(member.Id, backfill.Id), CancellationToken.None));

        var retried = (await context.Mediator.Send(new GitIntegrationJobRetryCommand(Owner, backfill.Id), CancellationToken.None))!;
        Assert.Equal((SharedDeliveryStatus.Pending, 0), (retried.Status, retried.Attempts));
        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Mediator.Send(new GitIntegrationJobRetryCommand(Owner, backfill.Id), CancellationToken.None));
    }

    [Fact]
    public async Task GitHub_App_Connection_Keeps_App_And_Installation()
    {
        var context = TestMediatorFactory.CreateGitContext();
        var created = await context.Mediator.Send(new GitHostConnectionCreateCommand(Owner, SharedProvider.GitHub, "Организация", "-----BEGIN RSA PRIVATE KEY-----\nabc\n-----END RSA PRIVATE KEY-----",
            null, SharedAuthKind.GitHubApp, 12345, 678), CancellationToken.None);
        Assert.Equal((SharedAuthKind.GitHubApp, 12345L, 678L), (created.AuthKind, created.AppId, created.InstallationId));
        // Переводы строк ключа сохраняются — иначе PEM не прочитается.
        Assert.Contains("\nabc\n", context.Git.Connections.Single().SecretProtected);

        var updated = (await context.Mediator.Send(new GitHostConnectionUpdateCommand(Owner, created.Id, "Организация", null, null, null, 999), CancellationToken.None))!;
        Assert.Equal((12345L, 999L), (updated.AppId, updated.InstallationId));

        await Assert.ThrowsAsync<ArgumentException>(() => context.Mediator.Send(new GitHostConnectionCreateCommand(Owner, SharedProvider.GitLab, "GitLab",
            "key", "https://gl.example.com", SharedAuthKind.GitHubApp, 1, 2), CancellationToken.None));
    }
}
