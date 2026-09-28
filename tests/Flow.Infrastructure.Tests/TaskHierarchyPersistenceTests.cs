using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskSearchQuery;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Flow.Infrastructure.Persistence;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Иерархия и ранг на реальном Postgres (docs/TZ_task_model.md §3, §7, этап 1B): рекурсивный CTE, COLLATE "C",
/// счётчики детей через join статусов, FK Restrict при удалении поддерева и проекта, unique (BoardId, Rank).
/// </summary>
[Collection(PostgresCollection.Name)]
public class TaskHierarchyPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    private async Task<BoardResponse> CreateBoardAsync(string key) =>
        (await db.SendAsync(new BoardCreateCommand(Owner, $"Board {key}", key))).Response!;

    private async Task<Guid> CreateAsync(BoardResponse board, TaskTypeKind kind, Guid? parentId = null, string? title = null) =>
        (await db.SendAsync(new TaskCreateCommand(Owner, board.Id, title ?? kind.ToString(), null, null,
            board.TaskTypes.First(t => t.Kind == kind).Id, ParentId: parentId)))!.Id;

    [Fact]
    public async Task Tree_Counts_And_Filter_Should_Come_From_Sql()
    {
        var board = await CreateBoardAsync("HTR");
        var epic = await CreateAsync(board, TaskTypeKind.Epic);
        var loose = await CreateAsync(board, TaskTypeKind.Task);
        var first = await CreateAsync(board, TaskTypeKind.Story, epic);
        var second = await CreateAsync(board, TaskTypeKind.Story, epic);
        var sub = await CreateAsync(board, TaskTypeKind.Task, first);
        await db.SendAsync(new TaskUpdateCommand(Owner, second, null, null, board.Statuses.Single(s => s.IsFinal).Id));

        // Вторую историю — перед первой: дерево обязано идти по рангу, а не по времени создания.
        await db.SendAsync(new TaskRankCommand(Owner, second, null, first));

        var tree = (await db.SendAsync(new TaskTreeQuery(Owner, board.Id)))!;
        Assert.Equal([(epic, 0), (second, 1), (first, 1), (sub, 2), (loose, 0)], tree.Select(n => (n.Task.Id, n.Depth)));

        var epicResponse = tree[0].Task;
        Assert.Equal((2, 1), (epicResponse.ChildCount, epicResponse.ChildDoneCount));

        var children = await db.SendAsync(new TaskSearchQuery(Owner, board.Id, Offset: 0, Sort: TaskSortField.Rank, Descending: false, ParentId: epic));
        Assert.Equal([second, first], children.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task Rank_Should_Compare_Bytewise_Not_By_Locale()
    {
        var board = await CreateBoardAsync("HCL");
        var lower = await CreateAsync(board, TaskTypeKind.Task, title: "lower");
        var upper = await CreateAsync(board, TaskTypeKind.Task, title: "upper");

        // По-байтово 'B' (66) < 'a' (97); по правилам локали было бы наоборот (a < B без учёта регистра).
        await db.InScopeAsync(async sp =>
        {
            var tasks = sp.GetRequiredService<ITaskItemRepository>();
            (await tasks.GetByIdAsync(lower, CancellationToken.None))!.SetRank("a0a");
            (await tasks.GetByIdAsync(upper, CancellationToken.None))!.SetRank("a0B");
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        });

        var tree = (await db.SendAsync(new TaskTreeQuery(Owner, board.Id)))!;
        Assert.Equal([upper, lower], tree.Select(n => n.Task.Id));

        var page = await db.SendAsync(new TaskSearchQuery(Owner, board.Id, Offset: 0, Sort: TaskSortField.Rank, Descending: false));
        Assert.Equal([upper, lower], page.Items.Select(t => t.Id));
    }

    [Fact]
    public async Task Same_Rank_Should_Surface_As_RankConflict()
    {
        var board = await CreateBoardAsync("HRC");
        var a = await CreateAsync(board, TaskTypeKind.Task);
        var b = await CreateAsync(board, TaskTypeKind.Task);

        await Assert.ThrowsAsync<RankConflictException>(() => db.InScopeAsync(async sp =>
        {
            var tasks = sp.GetRequiredService<ITaskItemRepository>();
            var first = (await tasks.GetByIdAsync(a, CancellationToken.None))!;
            (await tasks.GetByIdAsync(b, CancellationToken.None))!.SetRank(first.Rank);
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }));
    }

    [Fact]
    public async Task Rank_Change_Alone_Should_Not_Touch_UpdatedAt()
    {
        var board = await CreateBoardAsync("HUP");
        var a = await CreateAsync(board, TaskTypeKind.Task);
        var b = await CreateAsync(board, TaskTypeKind.Task);
        var before = (await db.SendAsync(new TaskGetQuery(Owner, b)))!.UpdatedAt;

        await db.SendAsync(new TaskRankCommand(Owner, b, null, a));

        Assert.Equal(before, (await db.SendAsync(new TaskGetQuery(Owner, b)))!.UpdatedAt);
    }

    [Fact]
    public async Task Cascade_Delete_And_Board_Delete_Should_Pass_Restrict_Fk()
    {
        var board = await CreateBoardAsync("HDL");
        var epic = await CreateAsync(board, TaskTypeKind.Epic);
        var story = await CreateAsync(board, TaskTypeKind.Story, epic);
        await CreateAsync(board, TaskTypeKind.Subtask, story);

        Assert.True(await db.SendAsync(new TaskDeleteCommand(Owner, epic, Cascade: true)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.TaskItems.CountAsync(t => t.BoardId == board.Id)));

        var other = await CreateBoardAsync("HDB");
        var otherEpic = await CreateAsync(other, TaskTypeKind.Epic);
        await CreateAsync(other, TaskTypeKind.Story, otherEpic);

        Assert.True(await db.SendAsync(new BoardDeleteCommand(Owner, other.Id)));
        Assert.False(await db.QueryAsync(ctx => ctx.Boards.AnyAsync(x => x.Id == other.Id)));
    }
}
