using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskRankCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetParentCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;
using Xunit;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Иерархия и ручной порядок (docs/TZ_task_model.md §3, §7, этап 1B): журнал, права, каскад, повтор при конфликте
/// ранга. Уровни и формат ключа — в Flow.Domain.Tests, рекурсивный CTE и COLLATE — в Flow.Infrastructure.Tests.
/// </summary>
public class TaskHierarchyFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator, string key = "PRJ") =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Проект", key), CancellationToken.None)).Response!;

    private static Guid TypeOf(BoardResponse board, SharedTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind).Id;

    private static async Task<Guid> CreateAsync(IMediator mediator, BoardResponse board, SharedTypeKind kind, Guid? parentId = null, Guid? actor = null) =>
        (await mediator.Send(new TaskCreateCommand(actor ?? Owner, board.Id, kind.ToString(), null, null, TypeOf(board, kind), ParentId: parentId), CancellationToken.None))!.Id;

    [Fact]
    public async Task Create_Under_Parent_Writes_ChildAdded_And_Counts_Children()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator);
        var epic = await CreateAsync(mediator, board, SharedTypeKind.Epic);

        var story = await CreateAsync(mediator, board, SharedTypeKind.Story, epic);
        await CreateAsync(mediator, board, SharedTypeKind.Story, epic);
        await mediator.Send(new TaskUpdateCommand(Owner, story, null, null, board.Statuses.Single(s => s.IsFinal).Id), CancellationToken.None);

        var response = (await mediator.Send(new TaskGetQuery(Owner, epic), CancellationToken.None))!;
        Assert.Equal(2, response.ChildCount);
        Assert.Equal(1, response.ChildDoneCount);
        Assert.Equal(epic, (await mediator.Send(new TaskGetQuery(Owner, story), CancellationToken.None))!.ParentId);
        Assert.Equal(2, activities.ForTask(epic).Count(a => a.Type == TaskActivityType.ChildAdded));
    }

    [Fact]
    public async Task Create_Under_Lower_Or_Foreign_Parent_Is_Rejected()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var other = await CreateBoardAsync(mediator, "OTH");
        var subtask = await CreateAsync(mediator, board, SharedTypeKind.Subtask);
        var foreignEpic = await CreateAsync(mediator, other, SharedTypeKind.Epic);

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(mediator, board, SharedTypeKind.Task, subtask));
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateAsync(mediator, board, SharedTypeKind.Story, foreignEpic));
    }

    [Fact]
    public async Task SetParent_Journals_Child_And_Both_Parents()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator);
        var first = await CreateAsync(mediator, board, SharedTypeKind.Epic);
        var second = await CreateAsync(mediator, board, SharedTypeKind.Epic);
        var story = await CreateAsync(mediator, board, SharedTypeKind.Story, first);

        var result = await mediator.Send(new TaskSetParentCommand(Owner, story, second), CancellationToken.None);

        Assert.Equal(second, result.Response!.ParentId);
        var changed = activities.ForTask(story).Single(a => a.Type == TaskActivityType.ParentChanged);
        Assert.Equal((first.ToString(), second.ToString()), (changed.OldValue, changed.NewValue));
        Assert.Single(activities.ForTask(first), a => a.Type == TaskActivityType.ChildRemoved);
        Assert.Single(activities.ForTask(second), a => a.Type == TaskActivityType.ChildAdded);

        // Тот же родитель — без записей; снять — ParentChanged в null.
        await mediator.Send(new TaskSetParentCommand(Owner, story, second), CancellationToken.None);
        Assert.Single(activities.ForTask(story), a => a.Type == TaskActivityType.ParentChanged);
        Assert.Null((await mediator.Send(new TaskSetParentCommand(Owner, story, null), CancellationToken.None)).Response!.ParentId);
    }

    [Fact]
    public async Task SetParent_To_Lower_Level_Leaves_No_Journal()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator);
        var story = await CreateAsync(mediator, board, SharedTypeKind.Story);
        var task = await CreateAsync(mediator, board, SharedTypeKind.Task);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskSetParentCommand(Owner, story, task), CancellationToken.None));
        Assert.DoesNotContain(activities.ForTask(story), a => a.Type == TaskActivityType.ParentChanged);
    }

    [Fact]
    public async Task ChangeType_Keeps_Level_Between_Parent_And_Children()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var epic = await CreateAsync(mediator, board, SharedTypeKind.Epic);
        var story = await CreateAsync(mediator, board, SharedTypeKind.Story, epic);
        await CreateAsync(mediator, board, SharedTypeKind.Task, story);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskUpdateCommand(Owner, story, null, null, null, TypeOf(board, SharedTypeKind.Subtask)), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskUpdateCommand(Owner, epic, null, null, null, TypeOf(board, SharedTypeKind.Task)), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_With_Children_Requires_Cascade_And_Removes_Subtree()
    {
        var context = TestMediatorFactory.CreateAttachmentContext();
        var board = await CreateBoardAsync(context.Mediator);
        var epic = await CreateAsync(context.Mediator, board, SharedTypeKind.Epic);
        var story = await CreateAsync(context.Mediator, board, SharedTypeKind.Story, epic);
        var task = await CreateAsync(context.Mediator, board, SharedTypeKind.Task, story);
        var bystander = await CreateAsync(context.Mediator, board, SharedTypeKind.Task);
        context.SearchIndex.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Mediator.Send(new TaskDeleteCommand(Owner, epic), CancellationToken.None));
        Assert.NotNull(await context.Tasks.GetByIdAsync(story, CancellationToken.None));

        Assert.True(await context.Mediator.Send(new TaskDeleteCommand(Owner, epic, Cascade: true), CancellationToken.None));

        foreach (var id in new[] { epic, story, task })
        {
            Assert.Null(await context.Tasks.GetByIdAsync(id, CancellationToken.None));
            Assert.Contains(context.SearchIndex.For(SearchSourceType.Task, id), r => r.Operation == SearchIndexOperation.Delete);
        }
        Assert.NotNull(await context.Tasks.GetByIdAsync(bystander, CancellationToken.None));
    }

    [Fact]
    public async Task Member_Cannot_Cascade_Delete_Someone_Elses_Subtask()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = User.Create("member", "member@example.com", "Имя", "Фамилия");
        member.MarkActive();
        users.Add(member);
        var board = await CreateBoardAsync(mediator);
        var mine = await CreateAsync(mediator, board, SharedTypeKind.Task, actor: member.Id);
        var foreign = await CreateAsync(mediator, board, SharedTypeKind.Subtask);
        await mediator.Send(new TaskSetParentCommand(Owner, foreign, mine), CancellationToken.None);

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskDeleteCommand(member.Id, mine, Cascade: true), CancellationToken.None));
    }

    [Fact]
    public async Task Tree_Goes_Parent_First_And_By_Rank()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var epic = await CreateAsync(mediator, board, SharedTypeKind.Epic);
        var loose = await CreateAsync(mediator, board, SharedTypeKind.Task);
        var story = await CreateAsync(mediator, board, SharedTypeKind.Story, epic);
        var sub = await CreateAsync(mediator, board, SharedTypeKind.Task, story);

        var tree = (await mediator.Send(new TaskTreeQuery(Owner, board.Id), CancellationToken.None))!;
        Assert.Equal([(epic, 0), (story, 1), (sub, 2), (loose, 0)], tree.Select(n => (n.Task.Id, n.Depth)));

        var branch = (await mediator.Send(new TaskTreeQuery(Owner, board.Id, story), CancellationToken.None))!;
        Assert.Equal([story, sub], branch.Select(n => n.Task.Id));
        Assert.Null(await mediator.Send(new TaskTreeQuery(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Rank_Moves_Task_Between_Neighbours()
    {
        var (mediator, _, tasks, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var a = await CreateAsync(mediator, board, SharedTypeKind.Task);
        var b = await CreateAsync(mediator, board, SharedTypeKind.Task);
        var c = await CreateAsync(mediator, board, SharedTypeKind.Task);

        async Task<Guid[]> Order() =>
            (await tasks.GetByBoardIdAsync(board.Id, null, CancellationToken.None)).OrderBy(t => t.Rank, StringComparer.Ordinal).Select(t => t.Id).ToArray();

        Assert.Equal([a, b, c], await Order());

        await mediator.Send(new TaskRankCommand(Owner, c, null, a), CancellationToken.None);
        Assert.Equal([c, a, b], await Order());

        await mediator.Send(new TaskRankCommand(Owner, c, a, null), CancellationToken.None);
        Assert.Equal([a, c, b], await Order());

        await mediator.Send(new TaskRankCommand(Owner, a, c, b), CancellationToken.None);
        Assert.Equal([c, a, b], await Order());

        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskRankCommand(Owner, a, null, null), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskRankCommand(Owner, a, a, null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskRankCommand(Owner, a, b, c), CancellationToken.None));
    }

    [Fact]
    public async Task Rank_Conflict_Is_Retried_With_Fresh_Key()
    {
        var (mediator, _, _, unitOfWork) = TestMediatorFactory.CreateWithUnitOfWork();
        var board = await CreateBoardAsync(mediator);
        var a = await CreateAsync(mediator, board, SharedTypeKind.Task);
        var b = await CreateAsync(mediator, board, SharedTypeKind.Task);
        var before = unitOfWork.SaveCount;

        unitOfWork.Failures.Enqueue(new RankConflictException(new Exception("23505")));
        var result = await mediator.Send(new TaskRankCommand(Owner, b, null, a), CancellationToken.None);

        Assert.NotNull(result.Response);
        Assert.Equal(2, unitOfWork.SaveCount - before);

        // Три конфликта подряд — отказ наружу, а не бесконечный цикл.
        for (var i = 0; i < 3; i++)
            unitOfWork.Failures.Enqueue(new RankConflictException(new Exception("23505")));
        await Assert.ThrowsAsync<RankConflictException>(() => mediator.Send(new TaskRankCommand(Owner, a, null, b), CancellationToken.None));
    }
}
