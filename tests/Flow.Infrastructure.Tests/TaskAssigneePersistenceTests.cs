using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Flow.Infrastructure.Tests;

[Collection(PostgresCollection.Name)]
public class TaskAssigneePersistenceTests(PostgresFixture db)
{
    [Fact]
    public async Task Assign_Should_PersistAssigneeId_And_FilterList()
    {
        var board = (await db.SendAsync(new BoardCreateCommand("Assign", "ASG"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(board.Id, "Mine", null, null)))!;
        await db.SendAsync(new TaskCreateCommand(board.Id, "Nobody's", null, null));
        var user = (await db.SendAsync(new UserCreateCommand("assignee", "assignee@example.com", "A", "B", "correct horse battery"))).Response!;

        var result = await db.SendAsync(new TaskAssignCommand(task.Id, user.Id));

        Assert.Equal(user.Id, result.Response!.AssigneeId);
        var stored = await db.QueryAsync(ctx => ctx.TaskItems.SingleAsync(t => t.Id == task.Id));
        Assert.Equal(user.Id, stored.AssigneeId);
        var mine = await db.SendAsync(new TaskListQuery(board.Id, user.Id));
        Assert.Equal(task.Id, Assert.Single(mine).Id);
    }

    [Fact]
    public async Task DeleteUser_Should_BeRejected_When_UserHasAssignedTasks()
    {
        // FK TaskItems.AssigneeId → Users с Restrict: ещё одно подтверждение, что пользователей деактивируем, а не удаляем.
        var board = (await db.SendAsync(new BoardCreateCommand("Restrict", "RST"))).Response!;
        var task = (await db.SendAsync(new TaskCreateCommand(board.Id, "Held", null, null)))!;
        var user = (await db.SendAsync(new UserCreateCommand("held.user", "held@example.com", "A", "B", "correct horse battery"))).Response!;
        await db.SendAsync(new TaskAssignCommand(task.Id, user.Id));

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            db.QueryAsync(ctx => ctx.Database.ExecuteSqlAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {user.Id}")));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }
}
