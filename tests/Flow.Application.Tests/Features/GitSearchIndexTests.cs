using System.Text;
using Flow.Application.Abstractions;
using Flow.Application.Tests.Fakes;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.GitIntegration;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Xunit;
using GitDevelopmentLinkKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using SharedProvider = Flow.Shared.Contracts.GitIntegration.GitProvider;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Этап 5E: PR и коммиты попадают в очередь поиска — новая связь и изменившийся заголовок; повтор того же события,
/// ветки и не изменившийся текст — нет; дозагрузка истории — фоновым приоритетом; удаление задачи убирает связи.
/// </summary>
public class GitSearchIndexTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private sealed record Setup(GitTestContext Context, BoardResponse Board, Guid RepositoryId, string Secret, Guid TaskId);

    private static async Task<Setup> ConnectAsync()
    {
        var context = TestMediatorFactory.CreateGitContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Сайт", "WEB"), CancellationToken.None)).Response!;
        var connection = await context.Mediator.Send(new GitHostConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "token", null), CancellationToken.None);
        var repository = (await context.Mediator.Send(new GitRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Форма входа", null, null), CancellationToken.None))!;
        await context.Mediator.Send(new GitBindCommand(Owner, board.Id, repository.Id, true), CancellationToken.None);
        return new Setup(context, board, repository.Id, context.Client.CreatedHooks.Single().Secret, task.Id);
    }

    private static async Task DeliverAsync(Setup setup, string eventName, string json, string delivery)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var headers = new Dictionary<string, string>
        {
            ["X-GitHub-Event"] = eventName,
            ["X-GitHub-Delivery"] = delivery,
            ["X-Hub-Signature-256"] = "sha256=" + GitSignatures.Sign(body, setup.Secret)
        };
        await setup.Context.Mediator.Send(new GitWebhookReceiveCommand(setup.RepositoryId, headers, body), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new GitDueIntegrationJobsQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new GitIntegrationJobProcessCommand(id), CancellationToken.None);
    }

    private static string Pr(string title) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            action = "edited",
            pull_request = new
            {
                number = 42, title, body = "", state = "open", merged = false, html_url = "https://github.com/acme/web/pull/42",
                user = new { login = "octocat" }, head = new { @ref = "WEB-1-forma" }, @base = new { @ref = "main" }, updated_at = "2026-09-21T10:00:00Z"
            }
        });

    private static string Push(string branch, string sha, string message) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            @ref = $"refs/heads/{branch}",
            after = sha,
            commits = new[] { new { id = sha, message, url = $"https://github.com/acme/web/commit/{sha}", timestamp = "2026-09-20T10:00:00Z", author = new { email = "x@example.com" } } }
        });

    private static List<EnqueuedRequest> Development(Setup setup) =>
        setup.Context.SearchIndex.All.Where(r => r.SourceType == SearchSourceType.Development).ToList();

    [Fact]
    public async Task New_Links_And_Changed_Titles_Are_Indexed_But_Branches_And_Repeats_Are_Not()
    {
        var setup = await ConnectAsync();

        await DeliverAsync(setup, "push", Push("WEB-1-forma", "aaa111", "поправил вёрстку"), "d-1");
        var commit = Assert.Single(Development(setup));
        Assert.Equal((SearchIndexOperation.Upsert, setup.Board.Id, 0), (commit.Operation, commit.BoardId, commit.Priority));
        var links = await setup.Context.Git.DevelopmentLinks.GetByTaskIdAsync(setup.TaskId, CancellationToken.None);
        Assert.Equal(links.Single(l => l.Kind == GitDevelopmentLinkKind.Commit).Id, commit.SourceId);

        // Тот же коммит повторным push'ем в ту же ветку — текст не изменился, в очередь не идёт.
        setup.Context.SearchIndex.Clear();
        await DeliverAsync(setup, "push", Push("WEB-1-forma", "aaa111", "поправил вёрстку"), "d-2");
        Assert.Empty(Development(setup));

        await DeliverAsync(setup, "pull_request", Pr("WEB-1 форма"), "d-3");
        Assert.Single(Development(setup));
        await DeliverAsync(setup, "pull_request", Pr("WEB-1 форма"), "d-4");
        Assert.Single(Development(setup));
        await DeliverAsync(setup, "pull_request", Pr("WEB-1 форма входа с капчей"), "d-5");
        Assert.Equal(2, Development(setup).Count);
    }

    [Fact]
    public async Task Backfill_Is_Indexed_In_The_Background_And_Task_Delete_Removes_Links()
    {
        var setup = await ConnectAsync();
        setup.Context.Client.History = new GitHistory(
            [new GitPullRequest("7", "WEB-1 старый фикс", null, GitDevelopmentLinkState.Merged, "https://github.com/acme/web/pull/7", "octocat", "web-1", "main", DateTime.UtcNow.AddDays(-3))],
            []);
        await setup.Context.Mediator.Send(new GitBackfillCommand(Owner, setup.RepositoryId), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new GitDueIntegrationJobsQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new GitIntegrationJobProcessCommand(id), CancellationToken.None);
        var backfilled = Assert.Single(Development(setup));
        Assert.Equal(1, backfilled.Priority);

        setup.Context.SearchIndex.Clear();
        Assert.True(await setup.Context.Mediator.Send(new TaskDeleteCommand(Owner, setup.TaskId), CancellationToken.None));
        var removed = Assert.Single(Development(setup));
        Assert.Equal((backfilled.SourceId, SearchIndexOperation.Delete), (removed.SourceId, removed.Operation));
    }
}
