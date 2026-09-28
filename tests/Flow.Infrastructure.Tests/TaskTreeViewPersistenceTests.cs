using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Xunit;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Infrastructure.Tests;

/// <summary>Дерево с фильтром на реальном Postgres (этап 2C): подходящие Id берёт тот же SQL-фильтр, что и список.</summary>
[Collection(PostgresCollection.Name)]
public class TaskTreeViewPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Filtered_Tree_Marks_Context_Ancestors()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Дерево", "TRV"))).Response!;
        Guid Type(SharedTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind).Id;
        var epic = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Эпик", null, null, Type(SharedTypeKind.Epic))))!.Id;
        var story = (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Нужная история", null, null, Type(SharedTypeKind.Story), ParentId: epic)))!.Id;
        await db.SendAsync(new TaskCreateCommand(Owner, board.Id, "Другая история", null, null, Type(SharedTypeKind.Story), ParentId: epic));

        var tree = (await db.SendAsync(new TaskTreeQuery(Owner, board.Id, Fql: "text ~ \"нужная\"")))!;

        Assert.Equal([(epic, true), (story, false)], tree.Select(n => (n.Task.Id, n.IsContextOnly)));
        Assert.Equal(2, tree[0].Progress!.Total);
    }
}
