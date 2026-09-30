using System.Text;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.Scm;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Scm;
using Xunit;
using SharedMode = Flow.Shared.Contracts.Boards.WorkflowMode;
using SharedProvider = Flow.Shared.Contracts.Scm.GitProvider;
using GitDevelopmentLinkKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using GitDevelopmentLinkState = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkState;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Этап 5C (docs/TZ_scm_integration.md §4–5): автопереходы по PR через workflow (разрешён / запрещён / задача уже
/// закрыта / переоткрытие), actor — автор PR или flow-bot, смарт-коммиты (только ветка по умолчанию, только
/// сопоставленный автор с правами, флаг привязки, без повтора), дозагрузка истории задачи не двигает.
/// </summary>
public class GitAutomationTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private sealed record Setup(GitTestContext Context, BoardResponse Board, Guid RepositoryId, string Secret, Guid TaskId)
    {
        public Guid Status(string name) => Board.Statuses.Single(s => s.Name == name).Id;
    }

    private static async Task<Setup> ConnectAsync(bool smartCommits = true)
    {
        var context = TestMediatorFactory.CreateGitContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Сайт", "WEB"), CancellationToken.None)).Response!;
        var connection = await context.Mediator.Send(new ScmConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "token", null), CancellationToken.None);
        var repository = (await context.Mediator.Send(new ScmRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Форма входа", null, null), CancellationToken.None))!;
        var setup = new Setup(context, board, repository.Id, context.Client.CreatedHooks.Single().Secret, task.Id);
        await context.Mediator.Send(new ScmBindCommand(Owner, board.Id, repository.Id, true,
            new UpdateScmBindingRequest(setup.Status("В работе"), setup.Status("Сделана"), smartCommits)), CancellationToken.None);
        return setup;
    }

    private static async Task DeliverAsync(Setup setup, string eventName, string json, string delivery)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var headers = new Dictionary<string, string>
        {
            ["X-GitHub-Event"] = eventName,
            ["X-GitHub-Delivery"] = delivery,
            ["X-Hub-Signature-256"] = "sha256=" + ScmSignatures.Sign(body, setup.Secret)
        };
        await setup.Context.Mediator.Send(new ScmWebhookReceiveCommand(setup.RepositoryId, headers, body), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new ScmDueDeliveriesQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new ScmDeliveryProcessCommand(id), CancellationToken.None);
    }

    private static string Pr(string action, string state, bool merged = false, string target = "main", string author = "octocat") =>
        $$$"""
        {"action":"{{{action}}}","pull_request":{"number":42,"title":"WEB-1 форма","body":"","state":"{{{state}}}","merged":{{{(merged ? "true" : "false")}}},
         "html_url":"https://github.com/acme/web/pull/42","user":{"login":"{{{author}}}"},"head":{"ref":"feature"},"base":{"ref":"{{{target}}}"},
         "updated_at":"2026-09-21T10:00:00Z"}}
        """;

    private static string Push(string branch, string sha, string message, string email = "owner@example.com") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            @ref = $"refs/heads/{branch}",
            after = sha,
            commits = new[] { new { id = sha, message, url = $"https://github.com/acme/web/commit/{sha}", timestamp = "2026-09-20T10:00:00Z", author = new { email } } }
        });

    private static async Task<TaskItem> TaskAsync(Setup setup) => (await setup.Context.Tasks.GetByIdAsync(setup.TaskId, CancellationToken.None))!;

    private static async Task<IReadOnlyList<Flow.Shared.Contracts.Tasks.TaskActivityResponse>> JournalAsync(Setup setup) =>
        (await setup.Context.Mediator.Send(new TaskActivityListQuery(Owner, setup.TaskId), CancellationToken.None))!;

    [Fact]
    public async Task Opened_And_Merged_Pull_Request_Move_The_Task_By_Bot_When_Author_Is_Unknown()
    {
        var setup = await ConnectAsync();

        await DeliverAsync(setup, "pull_request", Pr("opened", "open"), "pr-1");
        Assert.Equal(setup.Status("В работе"), (await TaskAsync(setup)).StatusId);
        var moved = (await JournalAsync(setup)).Last(a => a.Type == Flow.Shared.Contracts.Tasks.TaskActivityType.StatusChanged);
        Assert.Equal((GitIntegrationBot.Id, "PR #42", "https://github.com/acme/web/pull/42"), (moved.ActorId, moved.Source, moved.SourceUrl));
        Assert.NotNull(await setup.Context.Users.GetByIdAsync(GitIntegrationBot.Id, CancellationToken.None));

        // Влит в другую ветку — не переводит; в ветку по умолчанию — в «Сделана».
        await DeliverAsync(setup, "pull_request", Pr("closed", "closed", merged: true, target: "release"), "pr-2");
        Assert.Equal(setup.Status("В работе"), (await TaskAsync(setup)).StatusId);
        await DeliverAsync(setup, "pull_request", Pr("reopened", "open"), "pr-3");
        await DeliverAsync(setup, "pull_request", Pr("closed", "closed", merged: true), "pr-4");
        Assert.Equal(setup.Status("Сделана"), (await TaskAsync(setup)).StatusId);

        // Задача закрыта: переоткрытый PR назад не переводит.
        await DeliverAsync(setup, "pull_request", Pr("reopened", "open"), "pr-5");
        Assert.Equal(setup.Status("Сделана"), (await TaskAsync(setup)).StatusId);
    }

    [Fact]
    public async Task Mapped_Author_Is_The_Actor_And_Workflow_Refusal_Becomes_A_Note()
    {
        var setup = await ConnectAsync();
        (await setup.Context.Users.GetByIdAsync(Owner, CancellationToken.None))!.SetLink(UserLinkType.GitHub, "https://github.com/octocat");
        // В Restricted из «Не начата» в «В работе» нельзя без исполнителя.
        await setup.Context.Mediator.Send(new WorkflowSetCommand(Owner, setup.Board.Id, SharedMode.Restricted, [
            new TransitionRequest(null, setup.Status("В работе"), Conditions: new TransitionConditionsDto(RequireAssignee: true)),
            new TransitionRequest(null, setup.Status("Сделана"))
        ]), CancellationToken.None);

        await DeliverAsync(setup, "pull_request", Pr("opened", "open"), "pr-1");
        Assert.Equal(setup.Status("Не начата"), (await TaskAsync(setup)).StatusId);
        var link = setup.Context.Git.Links.Single(l => l.Kind == GitDevelopmentLinkKind.PullRequest);
        Assert.Contains("не разрешён workflow", link.Note);

        await DeliverAsync(setup, "pull_request", Pr("closed", "closed", merged: true), "pr-2");
        Assert.Equal(setup.Status("Сделана"), (await TaskAsync(setup)).StatusId);
        Assert.Null(link.Note);
        Assert.Equal(Owner, (await JournalAsync(setup)).Last(a => a.Type == Flow.Shared.Contracts.Tasks.TaskActivityType.StatusChanged).ActorId);
    }

    [Fact]
    public async Task Smart_Commits_Run_Once_On_Default_Branch_For_A_Mapped_Author()
    {
        var setup = await ConnectAsync();

        // Ветка задачи — не ветка по умолчанию: команды не выполняются.
        await DeliverAsync(setup, "push", Push("web-1-login", "a1", "WEB-1 #done"), "p-1");
        Assert.Equal(setup.Status("Не начата"), (await TaskAsync(setup)).StatusId);

        await DeliverAsync(setup, "push", Push("main", "b2", "WEB-1 #comment готово, см. WEB-9 #status \"На проверке\""), "p-2");
        Assert.Equal(setup.Status("На проверке"), (await TaskAsync(setup)).StatusId);
        var comment = Assert.Single((await setup.Context.Mediator.Send(new TaskCommentListQuery(Owner, setup.TaskId), CancellationToken.None))!);
        Assert.Equal(("готово, см. WEB-9", Owner), (comment.Body, comment.AuthorId));
        Assert.Contains(await JournalAsync(setup), a => a.Type == Flow.Shared.Contracts.Tasks.TaskActivityType.CommentAdded && a.Source == "коммит b2");

        // Тот же коммит в другой доставке (повторный push) — второго комментария нет.
        await DeliverAsync(setup, "push", Push("main", "b2", "WEB-1 #comment готово, см. WEB-9 #status \"На проверке\""), "p-3");
        Assert.Single((await setup.Context.Mediator.Send(new TaskCommentListQuery(Owner, setup.TaskId), CancellationToken.None))!);

        // Автор не сопоставлен — пометка на коммите, задача не тронута; неизвестный статус — тоже пометка.
        await DeliverAsync(setup, "push", Push("main", "c3", "WEB-1 #done", "stranger@example.com"), "p-4");
        Assert.Equal(setup.Status("На проверке"), (await TaskAsync(setup)).StatusId);
        Assert.Contains("не сопоставлен", setup.Context.Git.Links.Single(l => l.ExternalId == "c3").Note);
        await DeliverAsync(setup, "push", Push("main", "d4", "WEB-1 #status Архив"), "p-5");
        Assert.Contains("«Архив» нет", setup.Context.Git.Links.Single(l => l.ExternalId == "d4").Note);

        await DeliverAsync(setup, "push", Push("main", "e5", "WEB-1 #done"), "p-6");
        Assert.Equal(setup.Status("Сделана"), (await TaskAsync(setup)).StatusId);
    }

    [Fact]
    public async Task Smart_Commits_Need_The_Binding_Flag_And_History_Moves_Nothing()
    {
        var setup = await ConnectAsync(smartCommits: false);
        await DeliverAsync(setup, "push", Push("main", "a1", "WEB-1 #done"), "p-1");
        Assert.Equal(setup.Status("Не начата"), (await TaskAsync(setup)).StatusId);

        setup.Context.Client.History = new Flow.Application.Abstractions.ScmHistory(
            [new ScmPullRequest("7", "WEB-1", null, GitDevelopmentLinkState.Merged, "u", "octocat", "f", "main", DateTime.UtcNow)], []);
        await setup.Context.Mediator.Send(new ScmBackfillCommand(Owner, setup.RepositoryId), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new ScmDueDeliveriesQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new ScmDeliveryProcessCommand(id), CancellationToken.None);
        Assert.Contains(setup.Context.Git.Links, l => l.ExternalId == "7");
        Assert.Equal(setup.Status("Не начата"), (await TaskAsync(setup)).StatusId);

        // Статус автоперехода — только своего проекта.
        var other = (await setup.Context.Mediator.Send(new BoardCreateCommand(Owner, "Чужой", "OPS"), CancellationToken.None)).Response!;
        await Assert.ThrowsAsync<ArgumentException>(() => setup.Context.Mediator.Send(new ScmBindCommand(Owner, setup.Board.Id, setup.RepositoryId, true,
            new UpdateScmBindingRequest(other.Statuses[0].Id)), CancellationToken.None));
        var bound = (await setup.Context.Mediator.Send(new ScmBoardRepositoriesQuery(Owner, setup.Board.Id), CancellationToken.None))!.Single();
        Assert.Equal((setup.Status("В работе"), false), (bound.OnPullRequestOpenedStatusId!.Value, bound.SmartCommits));
    }
}
