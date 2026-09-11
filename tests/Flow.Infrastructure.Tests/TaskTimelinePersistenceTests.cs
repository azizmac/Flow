using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetDueDateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Tasks.Queries.TaskActivityListQuery;
using Flow.Application.Features.Tasks.Queries.TaskCommentListQuery;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Shared.Contracts.Tasks;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>Комментарии, упоминания и журнал на реальном Postgres: каскад при удалении доски, порядок, резолв @username.</summary>
[Collection(PostgresCollection.Name)]
public class TaskTimelinePersistenceTests(PostgresFixture db)
{
    [Fact]
    public async Task Comment_Should_Persist_Mentions_And_Activity_In_Order()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Timeline", "TML"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Discuss", null, null)))!;
        var user = (await db.SendAsync(new UserCreateCommand(PostgresFixture.OwnerId, "mentioned.one", "mentioned@example.com", "A", "B", "correct horse battery"))).Response!;

        await db.SendAsync(new TaskUpdateCommand(PostgresFixture.OwnerId, task.Id, "Discuss it", null, null));
        var comment = (await db.SendAsync(new TaskCommentAddCommand(PostgresFixture.OwnerId, task.Id, "Ping @Mentioned.One"))).Response!;

        Assert.Equal(user.Id, Assert.Single(comment.Mentions));
        var stored = await db.QueryAsync(ctx => ctx.TaskComments.SingleAsync(c => c.Id == comment.Id));
        Assert.Equal(user.Id, Assert.Single(stored.Mentions).UserId);

        var activity = (await db.SendAsync(new TaskActivityListQuery(task.Id)))!;
        Assert.Equal(
            new[] { TaskActivityType.Created, TaskActivityType.TitleChanged, TaskActivityType.CommentAdded },
            activity.Select(a => a.Type));
        Assert.Equal(comment.Id, Assert.Single((await db.SendAsync(new TaskCommentListQuery(task.Id)))!).Id);
    }

    [Fact]
    public async Task DeleteBoard_Should_Cascade_Comments_Mentions_And_Activity()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Cascade", "CSC"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "Doomed", null, null)))!;
        var user = (await db.SendAsync(new UserCreateCommand(PostgresFixture.OwnerId, "cascade.user", "cascade@example.com", "A", "B", "correct horse battery"))).Response!;
        var comment = (await db.SendAsync(new TaskCommentAddCommand(PostgresFixture.OwnerId, task.Id, "@cascade.user bye"))).Response!;
        Assert.Equal(user.Id, Assert.Single(comment.Mentions));

        var deleted = await db.SendAsync(new BoardDeleteCommand(PostgresFixture.OwnerId, board.Id));

        Assert.True(deleted);
        Assert.False(await db.QueryAsync(ctx => ctx.TaskComments.AnyAsync(c => c.TaskId == task.Id)));
        Assert.False(await db.QueryAsync(ctx => ctx.TaskActivities.AnyAsync(a => a.TaskId == task.Id)));
        Assert.Equal(0, await db.QueryAsync(ctx => ctx.Database.SqlQueryRaw<int>(
            "SELECT COUNT(*)::int AS \"Value\" FROM \"TaskCommentMentions\" WHERE \"CommentId\" = {0}", comment.Id).SingleAsync()));
    }

    [Fact]
    public async Task DeleteComment_Should_Remove_Row_And_Keep_Deleted_Activity()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Del comment", "DLC"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "T", null, null)))!;
        var comment = (await db.SendAsync(new TaskCommentAddCommand(PostgresFixture.OwnerId, task.Id, "temp"))).Response!;

        Assert.True(await db.SendAsync(new TaskCommentDeleteCommand(PostgresFixture.OwnerId, comment.Id)));

        Assert.False(await db.QueryAsync(ctx => ctx.TaskComments.AnyAsync(c => c.Id == comment.Id)));
        var activity = (await db.SendAsync(new TaskActivityListQuery(task.Id)))!;
        Assert.Contains(activity, a => a.Type == TaskActivityType.CommentDeleted && a.OldValue == comment.Id.ToString());
    }

    [Fact]
    public async Task DueDate_Should_Persist_As_Date()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Due", "DUE"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(PostgresFixture.OwnerId, board.Id, "T", null, null)))!;

        var result = await db.SendAsync(new TaskSetDueDateCommand(PostgresFixture.OwnerId, task.Id, new DateOnly(2026, 12, 31)));

        Assert.Equal(new DateOnly(2026, 12, 31), result.Response!.DueDate);
        var stored = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.Equal(new DateOnly(2026, 12, 31), stored.DueDate);
    }
}
