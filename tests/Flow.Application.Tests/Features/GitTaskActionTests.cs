using System.Text;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.GitIntegration;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.GitIntegration;
using Xunit;
using SharedProvider = Flow.Shared.Contracts.GitIntegration.GitProvider;
using SharedLinkState = Flow.Shared.Contracts.GitIntegration.GitDevelopmentLinkState;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Этап 5D (docs/TZ_git_integration.md §8): ветка и PR из карточки задачи — право WriteGit, только привязанный
/// репозиторий, связь сразу в «Разработке», автопереход «PR открыт»; комментарий «задача Flow» в новом PR по флагу
/// привязки, без повтора, если ссылка уже в описании, отказ хостинга — пометка у связи.
/// </summary>
public class GitTaskActionTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private sealed record Setup(GitTestContext Context, BoardResponse Board, Guid RepositoryId, string Secret, Guid TaskId)
    {
        public Guid Status(string name) => Board.Statuses.Single(s => s.Name == name).Id;
    }

    private static async Task<Setup> ConnectAsync(bool bind = true, UpdateGitBindingRequest? settings = null)
    {
        var context = TestMediatorFactory.CreateGitContext();
        var board = (await context.Mediator.Send(new BoardCreateCommand(Owner, "Сайт", "WEB"), CancellationToken.None)).Response!;
        var connection = await context.Mediator.Send(new GitHostConnectionCreateCommand(Owner, SharedProvider.GitHub, "GitHub", "token", null), CancellationToken.None);
        var repository = (await context.Mediator.Send(new GitRepositoryAddCommand(Owner, connection.Id, "101"), CancellationToken.None))!;
        var task = (await context.Mediator.Send(new TaskCreateCommand(Owner, board.Id, "Форма входа", null, null), CancellationToken.None))!;
        if (bind)
            await context.Mediator.Send(new GitBindCommand(Owner, board.Id, repository.Id, true, settings), CancellationToken.None);
        return new Setup(context, board, repository.Id, context.Client.CreatedHooks.Single().Secret, task.Id);
    }

    private static async Task DeliverAsync(Setup setup, string json, string delivery)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var headers = new Dictionary<string, string>
        {
            ["X-GitHub-Event"] = "pull_request",
            ["X-GitHub-Delivery"] = delivery,
            ["X-Hub-Signature-256"] = "sha256=" + GitSignatures.Sign(body, setup.Secret)
        };
        await setup.Context.Mediator.Send(new GitWebhookReceiveCommand(setup.RepositoryId, headers, body), CancellationToken.None);
        foreach (var id in await setup.Context.Mediator.Send(new GitDueIntegrationJobsQuery(DateTime.UtcNow.AddSeconds(1)), CancellationToken.None))
            await setup.Context.Mediator.Send(new GitIntegrationJobProcessCommand(id), CancellationToken.None);
    }

    private static string Pr(string number, string body, string state = "open") =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            action = "opened",
            pull_request = new
            {
                number = int.Parse(number), title = "WEB-1 форма", body, state, merged = false,
                html_url = $"https://github.com/acme/web/pull/{number}", user = new { login = "octocat" },
                head = new { @ref = "feature" }, @base = new { @ref = "main" }, updated_at = "2026-09-21T10:00:00Z"
            }
        });

    [Fact]
    public async Task Branch_Is_Created_On_The_Host_And_Linked_Right_Away()
    {
        var setup = await ConnectAsync();

        var development = await setup.Context.Mediator.Send(
            new TaskGitBranchCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "WEB-1-forma-vhoda", null), CancellationToken.None);

        var branch = Assert.Single(development!.Branches);
        Assert.Equal(("WEB-1-forma-vhoda", SharedLinkState.Open, Owner), (branch.ExternalId, branch.State, branch.AuthorUserId));
        Assert.Equal(("WEB-1-forma-vhoda", "main"), Assert.Single(setup.Context.Client.CreatedBranches));
        Assert.True(development.CanWrite);
        Assert.Equal(setup.RepositoryId, Assert.Single(development.Repositories!).Id);
    }

    [Fact]
    public async Task Bad_Branch_Name_And_Host_Refusal_Are_Bad_Requests()
    {
        var setup = await ConnectAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => setup.Context.Mediator.Send(
            new TaskGitBranchCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "bad name..", null), CancellationToken.None));

        setup.Context.Client.Failure = "Reference already exists";
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Context.Mediator.Send(
            new TaskGitBranchCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "WEB-1", null), CancellationToken.None));
        Assert.Contains("already exists", refused.Message);
        Assert.Empty((await setup.Context.Mediator.Send(new TaskDevelopmentQuery(Owner, setup.TaskId), CancellationToken.None))!.Branches);
    }

    [Fact]
    public async Task Pull_Request_Carries_The_Task_Link_And_Runs_Automation()
    {
        var setup = await ConnectAsync(settings: null);
        await setup.Context.Mediator.Send(new GitBindCommand(Owner, setup.Board.Id, setup.RepositoryId, true,
            new UpdateGitBindingRequest(setup.Status("В работе"), null, false, CommentOnPullRequests: true)), CancellationToken.None);

        var development = await setup.Context.Mediator.Send(
            new TaskGitPullRequestCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "WEB-1-forma", null, null, true), CancellationToken.None);

        var created = Assert.Single(setup.Context.Client.CreatedPullRequests);
        Assert.Equal(("WEB-1-forma", "main", "WEB-1 Форма входа", true), (created.Source, created.Target, created.Title, created.Draft));
        Assert.Contains("https://flow.example.com/tasks/WEB-1", created.Body);
        var pr = Assert.Single(development!.PullRequests);
        Assert.Equal("42", pr.ExternalId);
        Assert.Equal(setup.Status("В работе"), (await setup.Context.Tasks.GetByIdAsync(setup.TaskId, CancellationToken.None))!.StatusId);
        // Ссылка уже в описании — вебхук этого же PR комментарий не дублирует.
        await DeliverAsync(setup, Pr("42", created.Body), "d-1");
        Assert.Empty(setup.Context.Client.Comments);

        await Assert.ThrowsAsync<ArgumentException>(() => setup.Context.Mediator.Send(
            new TaskGitPullRequestCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "main", "main", null, false), CancellationToken.None));
    }

    [Fact]
    public async Task Member_Without_WriteGit_And_Unbound_Repository_Are_Refused()
    {
        var setup = await ConnectAsync(bind: false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Context.Mediator.Send(
            new TaskGitBranchCreateCommand(Owner, setup.TaskId, setup.RepositoryId, "WEB-1", null), CancellationToken.None));

        await setup.Context.Mediator.Send(new GitBindCommand(Owner, setup.Board.Id, setup.RepositoryId, true), CancellationToken.None);
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        setup.Context.Users.Add(member);

        await Assert.ThrowsAsync<ForbiddenException>(() => setup.Context.Mediator.Send(
            new TaskGitBranchCreateCommand(member.Id, setup.TaskId, setup.RepositoryId, "WEB-1", null), CancellationToken.None));
        Assert.False((await setup.Context.Mediator.Send(new TaskDevelopmentQuery(member.Id, setup.TaskId), CancellationToken.None))!.CanWrite);
        Assert.Empty(setup.Context.Client.CreatedBranches);
    }

    [Fact]
    public async Task New_Pull_Request_Gets_A_Comment_Only_When_Enabled_And_Refusal_Becomes_A_Note()
    {
        var setup = await ConnectAsync();
        await DeliverAsync(setup, Pr("7", "WEB-1"), "d-1");
        Assert.Empty(setup.Context.Client.Comments);

        await setup.Context.Mediator.Send(new GitBindCommand(Owner, setup.Board.Id, setup.RepositoryId, true,
            new UpdateGitBindingRequest(CommentOnPullRequests: true)), CancellationToken.None);
        await DeliverAsync(setup, Pr("8", "WEB-1"), "d-2");
        var comment = Assert.Single(setup.Context.Client.Comments);
        Assert.Equal("8", comment.Number);
        Assert.Contains("[WEB-1 Форма входа](https://flow.example.com/tasks/WEB-1)", comment.Body);

        // Повторное событие того же PR — связь уже есть, второго комментария нет.
        await DeliverAsync(setup, Pr("8", "WEB-1 обновлено"), "d-3");
        Assert.Single(setup.Context.Client.Comments);

        setup.Context.Client.Failure = "Resource not accessible by integration";
        await DeliverAsync(setup, Pr("9", "WEB-1"), "d-4");
        var development = (await setup.Context.Mediator.Send(new TaskDevelopmentQuery(Owner, setup.TaskId), CancellationToken.None))!;
        Assert.Contains("Комментарий в PR не оставлен", development.PullRequests.Single(p => p.ExternalId == "9").Note);
    }
}
