using System.Text;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Scm;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Интеграция с Git на реальном Postgres (этап 5A): доставка ложится в очередь с jsonb-payload и разбирается, повтор
/// с тем же Id не пишется, связи уникальны и уходят с задачей, отключение репозитория оставляет связи.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ScmPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Delivery_Is_Queued_Processed_And_Links_Follow_The_Task()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Git", "SCMP"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Связать", null, null)))!;
        var connection = ScmConnection.Create(ScmProvider.Gitea, "Gitea", "https://git.example.com", "p:tok", Owner);
        var repository = ScmRepository.Create(connection.Id, "9", "acme/scmp", "https://git.example.com/acme/scmp", "main", "p:hooksecret");
        await db.QueryAsync(async ctx =>
        {
            ctx.ScmConnections.Add(connection);
            ctx.ScmRepositories.Add(repository);
            ctx.ScmRepositoryBoards.Add(ScmRepositoryBoard.Create(repository.Id, board.Id, Owner));
            return await ctx.SaveChangesAsync();
        });

        var body = Encoding.UTF8.GetBytes("""{"ref":"refs/heads/main","after":"x","commits":[{"id":"s1","message":"SCMP-1 done","url":"https://git.example.com/c/s1","timestamp":"2026-09-20T10:00:00Z","author":{"email":"a@b.c"}}]}""");
        var headers = new Dictionary<string, string> { ["X-Gitea-Event"] = "push", ["X-Gitea-Delivery"] = "g-1", ["X-Gitea-Signature"] = ScmSignatures.Sign(body, "hooksecret") };

        Assert.Equal(ScmWebhookResult.Accepted, await db.SendAsync(new ScmWebhookReceiveCommand(repository.Id, headers, body)));
        Assert.Equal(ScmWebhookResult.Duplicate, await db.SendAsync(new ScmWebhookReceiveCommand(repository.Id, headers, body)));

        foreach (var id in await db.SendAsync(new ScmDueDeliveriesQuery(DateTime.UtcNow.AddSeconds(1))))
            await db.SendAsync(new ScmDeliveryProcessCommand(id));
        // Повторная обработка той же доставки не плодит связь.
        Assert.Equal(1, await db.QueryAsync(ctx => ctx.ScmLinks.CountAsync(l => l.TaskId == task.Id)));
        Assert.Equal(ScmDeliveryStatus.Done, await db.QueryAsync(ctx => ctx.ScmDeliveries.Where(d => d.RepositoryId == repository.Id).Select(d => d.Status).SingleAsync()));

        await db.SendAsync(new TaskDeleteCommand(Owner, task.Id));
        Assert.False(await db.QueryAsync(ctx => ctx.ScmLinks.AnyAsync(l => l.TaskId == task.Id)));
    }

    /// <summary>FQL development (этап 5B): открытый — Open или Draft, смёрженный, ни одного PR; очередь — «есть ли дозагрузка» и счётчик ошибок.</summary>
    [Fact]
    public async Task Development_Filter_And_Delivery_Diagnostics()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Разработка", "SCMD"))).Response!;
        var draft = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Черновик", null, null)))!;
        var merged = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Смёржена", null, null)))!;
        var none = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Без PR", null, null)))!;
        var branchOnly = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Только ветка", null, null)))!;
        var connection = ScmConnection.Create(ScmProvider.GitHub, "GitHub", null, "p:tok", Owner);
        var repository = ScmRepository.Create(connection.Id, "77", "acme/scmd", "https://github.com/acme/scmd", "main", "p:s");

        ScmLink Pr(Guid taskId, string number, ScmLinkState state)
        {
            var link = ScmLink.Create(taskId, repository.Id, ScmLinkKind.PullRequest, number);
            link.Apply("https://github.com/acme/scmd/pull/" + number, "PR", state, "octocat", null, "b", "main", DateTime.UtcNow);
            return link;
        }

        var failed = ScmDelivery.Create(repository.Id, "f-1", "push", "{}");
        for (var i = 0; i < ScmDelivery.MaxAttempts; i++)
            failed.MarkFailed("сбой", DateTime.UtcNow);
        await db.QueryAsync(async ctx =>
        {
            ctx.ScmConnections.Add(connection);
            ctx.ScmRepositories.Add(repository);
            ctx.ScmLinks.AddRange(Pr(draft.Id, "1", ScmLinkState.Draft), Pr(merged.Id, "2", ScmLinkState.Merged), Pr(merged.Id, "3", ScmLinkState.Closed),
                ScmLink.Create(branchOnly.Id, repository.Id, ScmLinkKind.Branch, "scmd-4"));
            ctx.ScmDeliveries.AddRange(failed, ScmDelivery.CreateBackfill(repository.Id));
            return await ctx.SaveChangesAsync();
        });

        async Task<Guid[]> Find(string fql) =>
            (await db.SendAsync(new TaskSearchQuery(Owner, board.Id, Fql: fql))).Items.Select(t => t.Id).Order().ToArray();

        Assert.Equal([draft.Id], await Find("development = openPR"));
        Assert.Equal([merged.Id], await Find("development = mergedPR"));
        Assert.Equal(new[] { none.Id, branchOnly.Id }.Order().ToArray(), await Find("development = noPR"));
        Assert.Equal(new[] { draft.Id, none.Id, branchOnly.Id }.Order().ToArray(), await Find("development != mergedPR"));

        await db.QueryAsync(async ctx =>
        {
            var store = new Flow.Infrastructure.Persistence.Repositories.ScmStore(ctx);
            Assert.True(await store.HasPendingDeliveryAsync(repository.Id, ScmDelivery.BackfillEvent, CancellationToken.None));
            Assert.False(await store.HasPendingDeliveryAsync(repository.Id, "push", CancellationToken.None));
            Assert.Equal(1, (await store.GetFailedDeliveryCountsAsync(CancellationToken.None))[repository.Id]);
            return 0;
        });
    }

    /// <summary>
    /// Этап 5C на Postgres: автопереход от flow-bot — профиль бота создаётся в той же транзакции, что и запись журнала
    /// (FK Restrict на Users), источник записи сохраняется; удалённый статус автоперехода обнуляет настройку (SetNull).
    /// </summary>
    [Fact]
    public async Task Bot_Transition_Persists_And_Deleted_Status_Turns_Automation_Off()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Автопереходы", "SCMA"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Перейти", null, null)))!;
        var review = board.Statuses.Single(s => s.Name == "На проверке").Id;
        var connection = ScmConnection.Create(ScmProvider.Gitea, "Gitea", "https://git.example.com", "p:tok", Owner);
        var repository = ScmRepository.Create(connection.Id, "31", "acme/scma", "https://git.example.com/acme/scma", "main", "p:hooksecret");
        var binding = ScmRepositoryBoard.Create(repository.Id, board.Id, Owner);
        binding.Configure(review, null, false);
        await db.QueryAsync(async ctx =>
        {
            ctx.ScmConnections.Add(connection);
            ctx.ScmRepositories.Add(repository);
            ctx.ScmRepositoryBoards.Add(binding);
            return await ctx.SaveChangesAsync();
        });

        var body = Encoding.UTF8.GetBytes("""{"action":"opened","pull_request":{"number":5,"title":"SCMA-1","state":"open","html_url":"https://git.example.com/acme/scma/pulls/5","user":{"login":"nobody"},"head":{"ref":"f"},"base":{"ref":"main"}}}""");
        var headers = new Dictionary<string, string> { ["X-Gitea-Event"] = "pull_request", ["X-Gitea-Delivery"] = "a-1", ["X-Gitea-Signature"] = ScmSignatures.Sign(body, "hooksecret") };
        Assert.Equal(ScmWebhookResult.Accepted, await db.SendAsync(new ScmWebhookReceiveCommand(repository.Id, headers, body)));
        foreach (var id in await db.SendAsync(new ScmDueDeliveriesQuery(DateTime.UtcNow.AddSeconds(1))))
            await db.SendAsync(new ScmDeliveryProcessCommand(id));

        Assert.Equal(review, await db.QueryAsync(ctx => ctx.TaskItems.Where(t => t.Id == task.Id).Select(t => t.StatusId).SingleAsync()));
        var entry = await db.QueryAsync(ctx => ctx.TaskActivities.SingleAsync(a => a.TaskId == task.Id && a.Type == TaskActivityType.StatusChanged));
        Assert.Equal((ScmBot.Id, "PR #5"), (entry.ActorId, entry.Source));
        Assert.Equal(UserStatus.Deactivated, await db.QueryAsync(ctx => ctx.Users.Where(u => u.Id == ScmBot.Id).Select(u => u.Status).SingleAsync()));

        await db.SendAsync(new StatusDeleteCommand(Owner, board.Id, review, board.Statuses.Single(s => s.Name == "В работе").Id));
        Assert.Null(await db.QueryAsync(ctx => ctx.ScmRepositoryBoards.Where(b => b.BoardId == board.Id).Select(b => b.OnPullRequestOpenedStatusId).SingleAsync()));
    }

    /// <summary>Этап 5D: флаг «комментарий в новом PR» — колонка привязки, по умолчанию выключен.</summary>
    [Fact]
    public async Task Comment_On_Pull_Requests_Flag_Is_Stored_On_The_Binding()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Комментарии PR", "SCMPC"))).Response!;
        var connection = ScmConnection.Create(ScmProvider.GitHub, "GitHub", null, "p:tok", Owner);
        var repository = ScmRepository.Create(connection.Id, "41", "acme/scmd", "https://github.com/acme/scmd", "main", "p:hooksecret");
        var plain = ScmRepositoryBoard.Create(repository.Id, board.Id, Owner);
        await db.QueryAsync(async ctx =>
        {
            ctx.ScmConnections.Add(connection);
            ctx.ScmRepositories.Add(repository);
            ctx.ScmRepositoryBoards.Add(plain);
            return await ctx.SaveChangesAsync();
        });
        Assert.False(await db.QueryAsync(ctx => ctx.ScmRepositoryBoards.Where(b => b.RepositoryId == repository.Id).Select(b => b.CommentOnPullRequests).SingleAsync()));

        await db.QueryAsync(async ctx =>
        {
            var binding = await ctx.ScmRepositoryBoards.AsTracking().SingleAsync(b => b.RepositoryId == repository.Id);
            binding.Configure(null, null, false, commentOnPullRequests: true);
            return await ctx.SaveChangesAsync();
        });
        Assert.True(await db.QueryAsync(ctx => ctx.ScmRepositoryBoards.Where(b => b.RepositoryId == repository.Id).Select(b => b.CommentOnPullRequests).SingleAsync()));
    }
}
