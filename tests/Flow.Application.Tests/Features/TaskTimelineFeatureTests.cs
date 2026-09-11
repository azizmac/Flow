using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Application.Features.Tasks.Queries.TaskGetQuery;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using Xunit;
using SharedActivityType = Flow.Shared.Contracts.Tasks.TaskActivityType;
using TaskActivityType = Flow.Domain.Entities.TaskActivityType;

namespace Flow.Application.Tests.Features;

/// <summary>Журнал изменений и комментарии задачи (docs/TZ_task_activity_comments.md).</summary>
public class TaskTimelineFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;

    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        users.Add(user);
        return user.Id;
    }

    private static async Task<BoardResponse> CreateBoardAsync(IMediator mediator, FakeBoardRepository boards, FakeTaskItemRepository tasks)
    {
        var board = (await mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!;
        tasks.RegisterBoardStatuses((await boards.GetByIdAsync(board.Id, CancellationToken.None))!);
        return board;
    }

    private static async Task<TaskResponse> CreateTaskAsync(IMediator mediator, Guid boardId, Guid? actor = null) =>
        (await mediator.Send(new TaskCreateCommand(actor ?? Owner, boardId, "Task", "desc", null), CancellationToken.None))!;

    // ---- activity ----

    [Fact]
    public async Task CreateTask_Should_Log_Created()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);

        var task = await CreateTaskAsync(mediator, board.Id);

        var entry = Assert.Single(activities.ForTask(task.Id));
        Assert.Equal(TaskActivityType.Created, entry.Type);
        Assert.Equal(Owner, entry.ActorId);
    }

    [Fact]
    public async Task UpdateTask_Should_Log_OneEntryPerChangedField()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var nextStatus = board.Statuses.First(s => s.Id != task.StatusId).Id;

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Renamed", "new desc", nextStatus), CancellationToken.None);

        var entries = activities.ForTask(task.Id).Where(a => a.Type != TaskActivityType.Created).ToList();
        Assert.Equal(3, entries.Count);
        var title = entries.Single(a => a.Type == TaskActivityType.TitleChanged);
        Assert.Equal("Task", title.OldValue);
        Assert.Equal("Renamed", title.NewValue);
        var description = entries.Single(a => a.Type == TaskActivityType.DescriptionChanged);
        Assert.Null(description.OldValue);
        Assert.Null(description.NewValue);
        var status = entries.Single(a => a.Type == TaskActivityType.StatusChanged);
        Assert.Equal(task.StatusId.ToString(), status.OldValue);
        Assert.Equal(nextStatus.ToString(), status.NewValue);
    }

    [Fact]
    public async Task UpdateTask_Should_NotLog_When_ValuesUnchanged()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Task", "desc", task.StatusId), CancellationToken.None);

        Assert.Single(activities.ForTask(task.Id));
    }

    [Fact]
    public async Task Assign_And_Unassign_Should_Log_AssigneeChanged()
    {
        var (mediator, boards, tasks, users, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var dev = AddUser(users, UserRole.Developer, "dev");

        await mediator.Send(new TaskAssignCommand(Owner, task.Id, dev), CancellationToken.None);
        await mediator.Send(new TaskAssignCommand(Owner, task.Id, dev), CancellationToken.None); // то же — без записи
        await mediator.Send(new TaskAssignCommand(Owner, task.Id, null), CancellationToken.None);

        var entries = activities.ForTask(task.Id).Where(a => a.Type == TaskActivityType.AssigneeChanged).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Null(entries[0].OldValue);
        Assert.Equal(dev.ToString(), entries[0].NewValue);
        Assert.Equal(dev.ToString(), entries[1].OldValue);
        Assert.Null(entries[1].NewValue);
    }

    [Fact]
    public async Task SetDueDate_Should_Persist_And_Log()
    {
        var (mediator, boards, tasks, _, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var due = new DateOnly(2026, 9, 15);

        var result = await mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, due), CancellationToken.None);
        await mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, due), CancellationToken.None); // повтор — без записи
        await mediator.Send(new TaskSetDueDateCommand(Owner, task.Id, null), CancellationToken.None);

        Assert.Equal(due, result.Response!.DueDate);
        var entries = activities.ForTask(task.Id).Where(a => a.Type == TaskActivityType.DueDateChanged).ToList();
        Assert.Equal(2, entries.Count);
        Assert.Equal("2026-09-15", entries[0].NewValue);
        Assert.Null(entries[1].NewValue);
        Assert.Null((await mediator.Send(new TaskGetQuery(task.Id), CancellationToken.None))!.DueDate);
    }

    [Fact]
    public async Task SetDueDate_Should_Return404_And_Respect_Permissions()
    {
        var (mediator, boards, tasks, users, _, _) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var reader = AddUser(users, UserRole.Reader, "reader");

        Assert.True((await mediator.Send(new TaskSetDueDateCommand(Owner, Guid.NewGuid(), null), CancellationToken.None)).IsNotFound);
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            mediator.Send(new TaskSetDueDateCommand(reader, task.Id, new DateOnly(2026, 1, 1)), CancellationToken.None));
    }

    [Fact]
    public async Task ActivityList_Should_ReturnOrdered_And_MapType()
    {
        var (mediator, boards, tasks, _, _, _) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        await mediator.Send(new TaskUpdateCommand(Owner, task.Id, "Renamed", null, null), CancellationToken.None);

        var list = await mediator.Send(new TaskActivityListQuery(task.Id), CancellationToken.None);

        Assert.NotNull(list);
        Assert.Equal(new[] { SharedActivityType.Created, SharedActivityType.TitleChanged }, list!.Select(a => a.Type));
        Assert.Null(await mediator.Send(new TaskActivityListQuery(Guid.NewGuid()), CancellationToken.None));
    }

    // ---- comments ----

    [Fact]
    public async Task AddComment_Should_ResolveMentions_And_Log()
    {
        var (mediator, boards, tasks, users, comments, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var dev = AddUser(users, UserRole.Developer, "dev.one");

        var result = await mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "Глянь, @Dev.One и @nobody"), CancellationToken.None);

        Assert.False(result.IsNotFound);
        var response = result.Response!;
        Assert.Equal(Owner, response.AuthorId);
        Assert.Equal("Глянь, @Dev.One и @nobody", response.Body);
        Assert.Equal(dev, Assert.Single(response.Mentions));
        Assert.Null(response.EditedAt);
        Assert.Single(comments.All);
        var logged = Assert.Single(activities.ForTask(task.Id), a => a.Type == TaskActivityType.CommentAdded);
        Assert.Equal(response.Id.ToString(), logged.NewValue);
        Assert.Equal(1, (await mediator.Send(new TaskGetQuery(task.Id), CancellationToken.None))!.CommentCount);
        Assert.Equal(1, Assert.Single(await mediator.Send(new TaskListQuery(board.Id, null), CancellationToken.None)).CommentCount);
    }

    [Fact]
    public async Task AddComment_Should_Return404_And_Reject_EmptyBody()
    {
        var (mediator, boards, tasks, _, _, _) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);

        Assert.True((await mediator.Send(new TaskCommentAddCommand(Owner, Guid.NewGuid(), "x"), CancellationToken.None)).IsNotFound);
        await Assert.ThrowsAsync<ArgumentException>(() => mediator.Send(new TaskCommentAddCommand(Owner, task.Id, "  "), CancellationToken.None));
    }

    [Fact]
    public async Task Reader_Cannot_Comment()
    {
        var (mediator, boards, tasks, users, _, _) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var reader = AddUser(users, UserRole.Reader, "reader");
        var member = AddUser(users, UserRole.Member, "member");

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCommentAddCommand(reader, task.Id, "x"), CancellationToken.None));
        Assert.False((await mediator.Send(new TaskCommentAddCommand(member, task.Id, "x"), CancellationToken.None)).IsNotFound);
    }

    [Fact]
    public async Task EditComment_OnlyAuthor_And_NoActivity()
    {
        var (mediator, boards, tasks, users, _, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var member = AddUser(users, UserRole.Member, "member");
        var comment = (await mediator.Send(new TaskCommentAddCommand(member, task.Id, "old"), CancellationToken.None)).Response!;
        var before = activities.ForTask(task.Id).Count;

        var edited = await mediator.Send(new TaskCommentEditCommand(member, comment.Id, "new"), CancellationToken.None);

        Assert.Equal("new", edited.Response!.Body);
        Assert.NotNull(edited.Response.EditedAt);
        Assert.Equal(before, activities.ForTask(task.Id).Count);
        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCommentEditCommand(Owner, comment.Id, "hijack"), CancellationToken.None));
        Assert.True((await mediator.Send(new TaskCommentEditCommand(member, Guid.NewGuid(), "x"), CancellationToken.None)).IsNotFound);
    }

    [Fact]
    public async Task DeleteComment_AuthorOrAdmin_And_Logs()
    {
        var (mediator, boards, tasks, users, comments, activities) = TestMediatorFactory.CreateWithTimeline();
        var board = await CreateBoardAsync(mediator, boards, tasks);
        var task = await CreateTaskAsync(mediator, board.Id);
        var member = AddUser(users, UserRole.Member, "member");
        var dev = AddUser(users, UserRole.Developer, "dev");
        var admin = AddUser(users, UserRole.Admin, "admin");
        var first = (await mediator.Send(new TaskCommentAddCommand(member, task.Id, "1"), CancellationToken.None)).Response!;
        var second = (await mediator.Send(new TaskCommentAddCommand(member, task.Id, "2"), CancellationToken.None)).Response!;

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCommentDeleteCommand(dev, first.Id), CancellationToken.None));
        Assert.True(await mediator.Send(new TaskCommentDeleteCommand(member, first.Id), CancellationToken.None));
        Assert.True(await mediator.Send(new TaskCommentDeleteCommand(admin, second.Id), CancellationToken.None));
        Assert.False(await mediator.Send(new TaskCommentDeleteCommand(admin, second.Id), CancellationToken.None));

        Assert.Empty(comments.All);
        var deleted = activities.ForTask(task.Id).Where(a => a.Type == TaskActivityType.CommentDeleted).ToList();
        Assert.Equal(new[] { first.Id.ToString(), second.Id.ToString() }, deleted.Select(a => a.OldValue));
        Assert.Empty((await mediator.Send(new TaskCommentListQuery(task.Id), CancellationToken.None))!);
    }

    [Fact]
    public async Task CommentList_Should_Return404_For_UnknownTask()
    {
        var (mediator, _, _, _, _, _) = TestMediatorFactory.CreateWithTimeline();

        Assert.Null(await mediator.Send(new TaskCommentListQuery(Guid.NewGuid()), CancellationToken.None));
    }
}
