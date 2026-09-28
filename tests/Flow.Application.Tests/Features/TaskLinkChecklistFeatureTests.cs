using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Tasks.Commands.TaskChecklistCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskLinkDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;
using Flow.Application.Features.Tasks.Queries.BoardBlockLinksQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using Xunit;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;
using BoardVisibility = Flow.Domain.Entities.BoardVisibility;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Связи (docs/TZ_task_model.md §5) и чек-лист (§8), этап 1C: журнал у обеих задач, «заблокирована», видимость
/// второй задачи, права. Инварианты сущностей — в Flow.Domain.Tests, каскады и SQL счётчиков — в Infrastructure.
/// </summary>
public class TaskLinkChecklistFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator, string key = "PRJ") =>
        (await mediator.Send(new BoardCreateCommand(Owner, $"Проект {key}", key), CancellationToken.None)).Response!;

    private static async Task<Guid> CreateTaskAsync(IMediator mediator, BoardResponse board, string title = "Задача", Guid? actor = null) =>
        (await mediator.Send(new TaskCreateCommand(actor ?? Owner, board.Id, title, null, null), CancellationToken.None))!.Id;

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    [Fact]
    public async Task Blocks_Link_Journals_Both_Sides_And_Counts_Open_Blockers_Only()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator);
        var blocker = await CreateTaskAsync(mediator, board, "Блокер");
        var blocked = await CreateTaskAsync(mediator, board, "Ждёт");

        var result = await mediator.Send(new TaskLinkCreateCommand(Owner, blocker, TaskLinkType.Blocks, blocked), CancellationToken.None);

        Assert.True(result.Response!.Link.Outward);
        Assert.False(result.Response.CycleWarning);
        Assert.Equal("Blocks", activities.ForTask(blocker).Single(a => a.Type == TaskActivityType.LinkAdded).OldValue);
        Assert.Equal("Blocks:in", activities.ForTask(blocked).Single(a => a.Type == TaskActivityType.LinkAdded).OldValue);
        Assert.Equal(1, (await mediator.Send(new TaskGetQuery(Owner, blocked), CancellationToken.None))!.BlockedByCount);

        // Закрытый блокер больше не блокирует.
        await mediator.Send(new TaskUpdateCommand(Owner, blocker, null, null, board.Statuses.Single(s => s.IsFinal).Id), CancellationToken.None);
        Assert.Equal(0, (await mediator.Send(new TaskGetQuery(Owner, blocked), CancellationToken.None))!.BlockedByCount);
    }

    [Fact]
    public async Task Inward_Code_Duplicate_And_Symmetric_RelatesTo()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var a = await CreateTaskAsync(mediator, board, "A");
        var b = await CreateTaskAsync(mediator, board, "B");

        // «A заблокирована задачей PRJ-2» — связь хранится как B → A.
        var inward = await mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, TargetCode: "prj-2", Inward: true), CancellationToken.None);
        Assert.False(inward.Response!.Link.Outward);
        Assert.Equal(1, (await mediator.Send(new TaskGetQuery(Owner, a), CancellationToken.None))!.BlockedByCount);

        Assert.True((await mediator.Send(new TaskLinkCreateCommand(Owner, b, TaskLinkType.Blocks, a), CancellationToken.None)).IsDuplicate);

        Assert.NotNull((await mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.RelatesTo, b), CancellationToken.None)).Response);
        Assert.True((await mediator.Send(new TaskLinkCreateCommand(Owner, b, TaskLinkType.RelatesTo, a), CancellationToken.None)).IsDuplicate);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, a), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, TargetCode: "PRJ-99"), CancellationToken.None));
    }

    [Fact]
    public async Task Blocks_Cycle_Is_A_Warning_Not_A_Refusal()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var a = await CreateTaskAsync(mediator, board, "A");
        var b = await CreateTaskAsync(mediator, board, "B");
        var c = await CreateTaskAsync(mediator, board, "C");

        await mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, b), CancellationToken.None);
        await mediator.Send(new TaskLinkCreateCommand(Owner, b, TaskLinkType.Blocks, c), CancellationToken.None);
        var closing = await mediator.Send(new TaskLinkCreateCommand(Owner, c, TaskLinkType.Blocks, a), CancellationToken.None);

        Assert.True(closing.Response!.CycleWarning);
    }

    [Fact]
    public async Task Hidden_Task_Cannot_Be_Linked_And_Is_Restricted_In_List()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var open = await CreateBoardAsync(mediator, "OPN");
        var hidden = await CreateBoardAsync(mediator, "SEC");
        var visibleTask = await CreateTaskAsync(mediator, open, "Видна");
        var secret = await CreateTaskAsync(mediator, hidden, "Секрет");
        await mediator.Send(new TaskLinkCreateCommand(Owner, visibleTask, TaskLinkType.RelatesTo, secret), CancellationToken.None);
        await mediator.Send(new BoardVisibilitySetCommand(Owner, hidden.Id, BoardVisibility.Private), CancellationToken.None);

        var links = (await mediator.Send(new TaskLinkListQuery(developer, visibleTask), CancellationToken.None))!;
        var peer = Assert.Single(links).Other;
        Assert.True(peer.Restricted);
        Assert.Null(peer.Code);
        Assert.Null(peer.Title);

        var another = await CreateTaskAsync(mediator, open, "Ещё");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new TaskLinkCreateCommand(developer, another, TaskLinkType.Blocks, TargetCode: "SEC-1"), CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Needs_Edit_On_Either_Side_And_Journals_Both()
    {
        var (mediator, _, _, users, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var member = AddUser(users, UserRole.Member, "member");
        var board = await CreateBoardAsync(mediator);
        var foreign = await CreateTaskAsync(mediator, board, "Чужая");
        var own = await CreateTaskAsync(mediator, board, "Своя", member);
        var link = (await mediator.Send(new TaskLinkCreateCommand(Owner, foreign, TaskLinkType.Blocks, own), CancellationToken.None)).Response!.Link;

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskLinkDeleteCommand(reader, link.Id), CancellationToken.None));

        // Member правит только свою задачу — но связь принадлежит и ей, этого достаточно.
        Assert.True(await mediator.Send(new TaskLinkDeleteCommand(member, link.Id), CancellationToken.None));
        Assert.Single(activities.ForTask(foreign), a => a.Type == TaskActivityType.LinkRemoved);
        Assert.Single(activities.ForTask(own), a => a.Type == TaskActivityType.LinkRemoved);
        Assert.False(await mediator.Send(new TaskLinkDeleteCommand(member, link.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Checklist_Journals_Only_Progress_And_Feeds_Task_Response()
    {
        var (mediator, _, _, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, board);

        var items = (await mediator.Send(new TaskChecklistAddCommand(Owner, task, "Макет"), CancellationToken.None))!;
        items = (await mediator.Send(new TaskChecklistAddCommand(Owner, task, "Вёрстка"), CancellationToken.None))!;
        var (first, second) = (items[0].Id, items[1].Id);

        items = (await mediator.Send(new TaskChecklistUpdateCommand(Owner, task, second, IsDone: true), CancellationToken.None))!;
        Assert.True(items[1].IsDone);
        Assert.Equal(Owner, items[1].DoneById);

        await mediator.Send(new TaskChecklistUpdateCommand(Owner, task, first, Text: "Макет v2"), CancellationToken.None);
        items = (await mediator.Send(new TaskChecklistReorderCommand(Owner, task, [second, first]), CancellationToken.None))!;
        Assert.Equal([second, first], items.Select(i => i.Id));

        var response = (await mediator.Send(new TaskGetQuery(Owner, task), CancellationToken.None))!;
        Assert.Equal((1, 2), (response.ChecklistDone, response.ChecklistTotal));

        var journal = activities.ForTask(task).Where(a => a.Type == TaskActivityType.ChecklistChanged).Select(a => (a.OldValue, a.NewValue)).ToList();
        Assert.Equal([("0/0", "0/1"), ("0/1", "0/2"), ("0/2", "1/2")], journal);

        await mediator.Send(new TaskChecklistDeleteCommand(Owner, task, second), CancellationToken.None);
        Assert.Equal(("1/2", "0/1"), activities.ForTask(task).Where(a => a.Type == TaskActivityType.ChecklistChanged).Select(a => (a.OldValue, a.NewValue)).Last());

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskChecklistDeleteCommand(Owner, task, Guid.NewGuid()), CancellationToken.None));
        Assert.Null(await mediator.Send(new TaskChecklistAddCommand(Owner, Guid.NewGuid(), "x"), CancellationToken.None));
    }

    [Fact]
    public async Task Reader_Cannot_Touch_Checklist()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var board = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, board);

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskChecklistAddCommand(reader, task, "x"), CancellationToken.None));
    }

    /// <summary>Стрелки роадмапа (этап 2H): только Blocks и только внутри проекта.</summary>
    [Fact]
    public async Task Board_Blocks_Are_Blocks_Within_The_Project()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var board = await CreateBoardAsync(mediator);
        var other = await CreateBoardAsync(mediator, "OTH");
        var a = await CreateTaskAsync(mediator, board, "A");
        var b = await CreateTaskAsync(mediator, board, "B");
        var c = await CreateTaskAsync(mediator, other, "C");
        await mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, b), CancellationToken.None);
        await mediator.Send(new TaskLinkCreateCommand(Owner, a, TaskLinkType.Blocks, c), CancellationToken.None);
        await mediator.Send(new TaskLinkCreateCommand(Owner, b, TaskLinkType.RelatesTo, a), CancellationToken.None);

        var edge = Assert.Single((await mediator.Send(new BoardBlockLinksQuery(Owner, board.Id), CancellationToken.None))!);
        Assert.Equal((a, b), (edge.SourceId, edge.TargetId));
    }
}
