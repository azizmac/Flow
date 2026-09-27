using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.StatusDeleteCommand;
using Flow.Application.Features.Boards.Workflow;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Domain.Entities;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Boards;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SharedMode = Flow.Shared.Contracts.Boards.WorkflowMode;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Workflow на реальном Postgres (этап 3B): замена графа целиком при unique (BoardId, From, To), NULLS NOT DISTINCT
/// у переходов «из любого», каскады при удалении статуса и проекта, проверка в смене статуса после перезагрузки.
/// </summary>
[Collection(PostgresCollection.Name)]
public class WorkflowPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Workflow_Should_Be_Replaced_Persisted_And_Enforced()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Workflow", "WFP"))).Response!;
        Guid Id(string name) => board.Statuses.Single(s => s.Name == name).Id;
        var (todo, doing, review, done) = (Id("Не начата"), Id("В работе"), Id("На проверке"), Id("Сделана"));

        await db.SendAsync(new WorkflowSetCommand(Owner, board.Id, SharedMode.Restricted,
            [new(todo, doing), new(doing, review), new(review, done), new(null, todo, "В очередь")]));

        // Повторная замена с теми же парами — EF обязан удалить старые строки раньше вставки новых.
        var saved = (await db.SendAsync(new WorkflowSetCommand(Owner, board.Id, SharedMode.Restricted,
            [new(todo, doing, Conditions: new TransitionConditionsDto(RequireAssignee: true)), new(doing, review), new(review, done), new(null, todo), new(null, done)])))!;
        Assert.Equal(5, saved.Transitions.Count);

        var read = (await db.SendAsync(new WorkflowGetQuery(Owner, board.Id)))!;
        Assert.Equal(SharedMode.Restricted, read.Mode);
        Assert.True(read.Transitions.Single(t => t.FromStatusId == todo).Conditions.RequireAssignee);

        var task = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "T", null, null)))!.Id;
        Assert.NotNull((await db.SendAsync(new TaskUpdateCommand(Owner, task, null, null, done))).Response); // «из любого»
        Assert.NotNull((await db.SendAsync(new TaskUpdateCommand(Owner, task, null, null, todo))).Response);
        Assert.NotNull((await db.SendAsync(new TaskUpdateCommand(Owner, task, null, null, doing))).Reasons); // нужен исполнитель

        // Удаление статуса уносит его переходы.
        await db.SendAsync(new StatusDeleteCommand(Owner, board.Id, review, doing));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.Set<StatusTransition>().CountAsync(t => t.FromStatusId == review || t.ToStatusId == review)));

        Assert.True(await db.SendAsync(new BoardDeleteCommand(Owner, board.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.Set<StatusTransition>().CountAsync(t => t.BoardId == board.Id)));
    }

    [Fact]
    public async Task Two_Global_Transitions_To_One_Status_Should_Hit_Unique_Index()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Workflow nulls", "WFN"))).Response!;
        var done = board.Statuses.Single(s => s.IsFinal).Id;

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.InScopeAsync(async sp =>
        {
            var ctx = sp.GetRequiredService<FlowDbContext>();
            var tracked = await ctx.Boards.Include(b => b.Statuses).Include(b => b.Transitions).SingleAsync(b => b.Id == board.Id);
            tracked.SetWorkflow(Domain.Entities.WorkflowMode.Free, [new TransitionSpec(null, done)]);
            await ctx.SaveChangesAsync();
            // Домен дубль не пустит — вставляем вторую строку в обход агрегата, чтобы проверить сам индекс.
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"""INSERT INTO "StatusTransitions" ("Id", "BoardId", "FromStatusId", "ToStatusId", "RequireAssignee", "RequireChildrenDone", "RequireChecklistDone") VALUES ({Guid.NewGuid()}, {board.Id}, NULL, {done}, false, false, false)""");
        }));
        Assert.Equal(Npgsql.PostgresErrorCodes.UniqueViolation, error.SqlState);
    }
}
