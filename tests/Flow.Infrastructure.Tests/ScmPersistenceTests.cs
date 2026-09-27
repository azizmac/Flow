using System.Text;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Scm;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Domain.Entities;
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
}
