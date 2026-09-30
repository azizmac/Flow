using System.Text;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Scm;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Scm;
using Xunit;
using DomainState = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkState;
using GitDevelopmentLinkKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using SharedProvider = Flow.Shared.Contracts.Scm.GitProvider;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Интеграция с Git в Application (docs/TZ_scm_integration.md, этап 5A): подключения и вебхук (Admin+), ручная
/// настройка при отказе хостинга, приём доставки (подпись, повтор, лишние события), разбор push/PR/веток только в
/// привязанных проектах, алиас кода, авторы, блок «Разработка» и значок PR. Разбор payload — ScmParsingTests.
/// </summary>
public class ScmFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private sealed record Setup(ScmTestContext Context, BoardResponse Board, Guid RepositoryId, string Secret);

    private static async Task<Setup> ConnectAsync(string key = "WEB", bool bind = true)
    {
        var context = TestMediatorFactory.CreateScmContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Сайт", key), CancellationToken.None)).Response!;
        var connection = await context.Mediator.Send(new ScmConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "token", null), CancellationToken.None);
        var repository = (await context.Mediator.Send(new ScmRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        if (bind)
            await context.Mediator.Send(new ScmBindCommand(Owner, board.Id, repository.Id, true), CancellationToken.None);
        return new Setup(context, board, repository.Id, context.Client.CreatedHooks.Single().Secret);
    }

    private static async Task<ScmWebhookResult> DeliverAsync(Setup setup, string eventName, string json, string delivery = "d-1", string? secret = null)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var headers = new Dictionary<string, string>
        {
            ["X-GitHub-Event"] = eventName,
            ["X-GitHub-Delivery"] = delivery,
            ["X-Hub-Signature-256"] = "sha256=" + ScmSignatures.Sign(body, secret ?? setup.Secret)
        };
        var result = await setup.Context.Mediator.Send(new ScmWebhookReceiveCommand(setup.RepositoryId, headers, body), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new ScmDueDeliveriesQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new ScmDeliveryProcessCommand(id), CancellationToken.None);
        return result;
    }

    private static string Push(string branch, params (string Sha, string Message)[] commits)
    {
        var items = commits.Select(c => System.Text.Json.JsonSerializer.Serialize(new
        {
            id = c.Sha,
            message = c.Message,
            url = $"https://github.com/acme/web/commit/{c.Sha}",
            timestamp = "2026-09-20T10:00:00Z",
            author = new { email = "owner@example.com", username = "octocat" }
        }));
        return $"{{\"ref\":\"refs/heads/{branch}\",\"after\":\"x\",\"commits\":[{string.Join(",", items)}]}}";
    }

    [Fact]
    public async Task Connection_Is_Checked_And_Webhook_Created_With_A_Secret()
    {
        var setup = await ConnectAsync();
        var connection = Assert.Single(await setup.Context.Mediator.Send(new ScmConnectionListQuery(Owner), CancellationToken.None));

        Assert.Equal(("octocat", false), (connection.CheckedLogin, connection.NeedsReconnect));
        var repository = Assert.Single(connection.Repositories);
        Assert.True(repository.WebhookCreated);
        Assert.Equal([setup.Board.Id], repository.BoardIds);
        var hook = setup.Context.Client.CreatedHooks.Single();
        Assert.Equal($"https://flow.example.com/hooks/scm/{setup.RepositoryId}", hook.Url);
        Assert.Equal(64, hook.Secret.Length);
        // Токен хранится только зашифрованным.
        Assert.StartsWith("p:", setup.Context.Scm.Connections.Single().SecretProtected);
    }

    [Fact]
    public async Task Failed_Webhook_Returns_Manual_Settings_And_Bad_Token_Is_Reported()
    {
        var context = TestMediatorFactory.CreateScmContext();
        var connection = await context.Mediator.Send(new ScmConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "bad", null), CancellationToken.None);
        Assert.NotNull(connection.LastError);

        context.Client.Failure = "Токену не хватает прав на это действие.";
        var repository = (await context.Mediator.Send(new ScmRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        Assert.False(repository.WebhookCreated);
        Assert.Equal($"https://flow.example.com/hooks/scm/{repository.Id}", repository.ManualWebhookUrl);
        Assert.NotNull(repository.ManualWebhookSecret);
        Assert.Contains("прав", repository.WebhookError);
    }

    [Fact]
    public async Task Only_Admins_Manage_Integrations()
    {
        var context = TestMediatorFactory.CreateScmContext();
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        context.Users.Add(member);

        await Assert.ThrowsAsync<ForbiddenException>(() => context.Mediator.Send(new ScmConnectionListQuery(member.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Webhook_Checks_Signature_Duplicates_And_Unknown_Events()
    {
        var setup = await ConnectAsync();

        Assert.Equal(ScmWebhookResult.Unauthorized, await DeliverAsync(setup, "push", Push("main"), secret: "wrong"));
        Assert.Equal(ScmWebhookResult.Accepted, await DeliverAsync(setup, "push", Push("main")));
        Assert.Equal(ScmWebhookResult.Duplicate, await DeliverAsync(setup, "push", Push("main")));
        Assert.Equal(ScmWebhookResult.Ignored, await DeliverAsync(setup, "star", "{}", "d-2"));
        Assert.Equal(ScmWebhookResult.NotFound, await setup.Context.Mediator.Send(
            new ScmWebhookReceiveCommand(Guid.NewGuid(), new Dictionary<string, string>(), []), CancellationToken.None));
    }

    [Fact]
    public async Task Push_And_Pull_Request_Link_Tasks_Of_Bound_Projects_Only()
    {
        var setup = await ConnectAsync();
        var mediator = setup.Context.Mediator;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, setup.Board.Id, "Форма входа", null, null), CancellationToken.None))!;
        var other = (await mediator.Send(new BoardCreateCommand(Owner, "Чужой", "OPS"), CancellationToken.None)).Response!;
        var foreign = (await mediator.Send(new TaskCreateCommand(Owner, other.Id, "Не наша", null, null), CancellationToken.None))!;

        await DeliverAsync(setup, "push", Push("web-1-login", ("c1", "add form"), ("c2", "OPS-1 unrelated")), "p-1");
        await DeliverAsync(setup, "pull_request", """
            {"action":"opened","pull_request":{"number":42,"title":"Login","body":"Closes WEB-1","state":"open","merged":false,
             "html_url":"https://github.com/acme/web/pull/42","user":{"login":"octocat"},"head":{"ref":"web-1-login"},"base":{"ref":"main"},
             "updated_at":"2026-09-21T10:00:00Z"}}
            """, "pr-1");

        var development = (await mediator.Send(new TaskDevelopmentQuery(Owner, task.Id), CancellationToken.None))!;
        Assert.Equal(["web-1-login"], development.Branches.Select(b => b.ExternalId));
        // Оба коммита — в ветке задачи; второй упоминает задачу непривязанного проекта, ей связь не достаётся.
        Assert.Equal(2, development.CommitCount);
        Assert.Equal(("42", Flow.Shared.Contracts.Scm.GitDevelopmentLinkState.Open, "acme/web"),
            (development.PullRequests.Single().ExternalId, development.PullRequests.Single().State, development.PullRequests.Single().RepositoryName));
        Assert.Equal(Owner, development.Commits.First().AuthorUserId);
        Assert.Empty((await mediator.Send(new TaskDevelopmentQuery(Owner, foreign.Id), CancellationToken.None))!.Commits);

        // Значок PR в ответе задачи и обновление состояния при merge.
        await DeliverAsync(setup, "pull_request", """
            {"action":"closed","pull_request":{"number":42,"title":"Login","body":"Closes WEB-1","state":"closed","merged":true,
             "html_url":"https://github.com/acme/web/pull/42","user":{"login":"octocat"},"head":{"ref":"web-1-login"},"base":{"ref":"main"},
             "updated_at":"2026-09-22T10:00:00Z"}}
            """, "pr-2");
        Assert.Equal(Flow.Shared.Contracts.Scm.GitDevelopmentLinkState.Merged, (await mediator.Send(new TaskGetQuery(Owner, task.Id), CancellationToken.None))!.PullRequestState);
        Assert.Single(setup.Context.Scm.Links, l => l.Kind == GitDevelopmentLinkKind.PullRequest);

        // Удалённая ветка закрывает связь, но не удаляет её.
        await DeliverAsync(setup, "delete", """{"ref":"web-1-login","ref_type":"branch"}""", "del-1");
        Assert.Equal(DomainState.Closed, setup.Context.Scm.Links.Single(l => l.Kind == GitDevelopmentLinkKind.Branch).State);
    }

    [Fact]
    public async Task Old_Code_After_Move_Still_Links_And_Unbound_Repository_Links_Nothing()
    {
        var setup = await ConnectAsync();
        var mediator = setup.Context.Mediator;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, setup.Board.Id, "Переедет", null, null), CancellationToken.None))!;
        var target = (await mediator.Send(new BoardCreateCommand(Owner, "Новый дом", "NEW"), CancellationToken.None)).Response!;
        await mediator.Send(new ScmBindCommand(Owner, target.Id, setup.RepositoryId, true), CancellationToken.None);
        await mediator.Send(new TaskMoveCommand(Owner, task.Id, target.Id), CancellationToken.None);

        await DeliverAsync(setup, "push", Push("main", ("c9", "WEB-1 fix after move")), "p-9");
        Assert.Equal(1, (await mediator.Send(new TaskDevelopmentQuery(Owner, task.Id), CancellationToken.None))!.CommitCount);

        await mediator.Send(new ScmBindCommand(Owner, target.Id, setup.RepositoryId, false), CancellationToken.None);
        await DeliverAsync(setup, "push", Push("main", ("c10", "NEW-1 again")), "p-10");
        Assert.Equal(1, (await mediator.Send(new TaskDevelopmentQuery(Owner, task.Id), CancellationToken.None))!.CommitCount);
    }
}
