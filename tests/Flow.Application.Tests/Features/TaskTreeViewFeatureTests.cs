using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskTreeQuery;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using SharedTypeKind = Flow.Shared.Contracts.Boards.TaskTypeKind;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Экран дерева (docs/TZ_task_views.md §3, этап 2C): фильтр с «контекстными» предками, прогресс поддерева по задачам
/// и story points, глубина для ленивого раскрытия. Порядок обхода — TaskHierarchyFeatureTests и рекурсивный CTE.
/// </summary>
public class TaskTreeViewFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<(IMediator Mediator, BoardResponse Board, Guid Epic, Guid Story, Guid Sub, Guid Loose)> ArrangeAsync()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PRJ"), CancellationToken.None)).Response!;
        Guid Type(SharedTypeKind kind) => board.TaskTypes.First(t => t.Kind == kind).Id;
        async Task<Guid> Create(string title, SharedTypeKind kind, Guid? parent = null) =>
            (await mediator.Send(new TaskCreateCommand(Owner, board.Id, title, null, null, Type(kind), ParentId: parent), CancellationToken.None))!.Id;

        var epic = await Create("Эпик релиза", SharedTypeKind.Epic);
        var story = await Create("История входа", SharedTypeKind.Story, epic);
        var sub = await Create("Поправить кнопку", SharedTypeKind.Task, story);
        var loose = await Create("Отдельная задача", SharedTypeKind.Task);
        return (mediator, board, epic, story, sub, loose);
    }

    [Fact]
    public async Task Filter_Keeps_Ancestors_As_Context_And_Hides_Other_Branches()
    {
        var (mediator, board, epic, story, sub, _) = await ArrangeAsync();

        var tree = (await mediator.Send(new TaskTreeQuery(Owner, board.Id, Query: "кнопку"), CancellationToken.None))!;

        Assert.Equal([epic, story, sub], tree.Select(n => n.Task.Id));
        Assert.Equal([true, true, false], tree.Select(n => n.IsContextOnly));
        Assert.Equal([1, 1, 0], tree.Select(n => n.VisibleChildCount));
    }

    [Fact]
    public async Task Progress_Counts_Whole_Subtree_Tasks_And_Points()
    {
        var (mediator, board, epic, story, sub, _) = await ArrangeAsync();
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        await mediator.Send(new TaskSetEstimateCommand(Owner, story, 5m, null), CancellationToken.None);
        await mediator.Send(new TaskSetEstimateCommand(Owner, sub, 3m, null), CancellationToken.None);
        await mediator.Send(new TaskUpdateCommand(Owner, sub, null, null, done), CancellationToken.None);

        var tree = (await mediator.Send(new TaskTreeQuery(Owner, board.Id), CancellationToken.None))!;

        var epicProgress = tree.Single(n => n.Task.Id == epic).Progress!;
        Assert.Equal((2, 1, 8m, 3m), (epicProgress.Total, epicProgress.Done, epicProgress.Points, epicProgress.DonePoints));
        Assert.Equal(0, tree.Single(n => n.Task.Id == sub).Progress!.Total);
    }

    [Fact]
    public async Task MaxDepth_Cuts_Levels_And_Subtree_Loads_Them()
    {
        var (mediator, board, epic, story, sub, loose) = await ArrangeAsync();

        var top = (await mediator.Send(new TaskTreeQuery(Owner, board.Id, MaxDepth: 1), CancellationToken.None))!;
        Assert.Equal([epic, story, loose], top.Select(n => n.Task.Id));
        Assert.Equal(1, top.Single(n => n.Task.Id == story).VisibleChildCount);

        var branch = (await mediator.Send(new TaskTreeQuery(Owner, board.Id, RootId: story, MaxDepth: 1), CancellationToken.None))!;
        Assert.Equal([(story, 0), (sub, 1)], branch.Select(n => (n.Task.Id, n.Depth)));
    }
}
