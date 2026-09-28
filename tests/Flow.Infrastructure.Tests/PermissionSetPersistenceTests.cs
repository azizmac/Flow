using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Groups;
using Flow.Application.Features.PermissionSets;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SharedPermission = Flow.Shared.Contracts.Boards.ProjectPermission;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Наборы прав на Postgres (этап 4E): права — int[], участия прямые и через группы приходят списком с набором каждое
/// (коллекции в одном запросе), удалённый набор снимается с участий (SetNull) — остаются права роли.
/// </summary>
[Collection(PostgresCollection.Name)]
public class PermissionSetPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Sets_Drive_Access_And_Fall_Back_To_Role_When_Deleted()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Наборы", "PSETP"))).Response!;
        await db.SendAsync(new BoardMemberSetCommand(Owner, board.Id, Owner, ProjectRole.Admin));
        await db.SendAsync(new BoardVisibilitySetCommand(Owner, board.Id, BoardVisibility.Private));
        var user = (await db.SendAsync(new UserCreateCommand(Owner, "pset.user", "pset.user@example.com", "Имя", "Фамилия", Password: "password123"))).Response!.Id;

        var commenter = await db.SendAsync(new PermissionSetCreateCommand(Owner, "Комментатор PSETP", null, ProjectRole.Viewer, [ProjectPermission.Comment]));
        var attacher = await db.SendAsync(new PermissionSetCreateCommand(Owner, "Файлы PSETP", null, ProjectRole.Viewer, [ProjectPermission.Attach]));
        var stored = await db.QueryAsync(ctx => ctx.PermissionSets.AsNoTracking().SingleAsync(s => s.Id == commenter.Id));
        Assert.Equal([ProjectPermission.ViewProject, ProjectPermission.Comment], stored.Permissions);

        var group = await db.SendAsync(new GroupCreateCommand(Owner, "Группа PSETP", null));
        await db.SendAsync(new GroupMemberSetCommand(Owner, group.Id, user, true));
        await db.SendAsync(new BoardMemberSetCommand(Owner, board.Id, user, ProjectRole.Viewer, commenter.Id));
        await db.SendAsync(new BoardGroupSetCommand(Owner, board.Id, group.Id, ProjectRole.Viewer, attacher.Id));

        var access = Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, board.Id)));
        Assert.Contains(SharedPermission.Comment, access.Permissions);
        Assert.Contains(SharedPermission.Attach, access.Permissions);
        Assert.DoesNotContain(SharedPermission.CreateTask, access.Permissions);

        Assert.True(await db.SendAsync(new PermissionSetDeleteCommand(Owner, commenter.Id)));
        Assert.Null(await db.QueryAsync(ctx => ctx.BoardMembers.Where(m => m.BoardId == board.Id && m.UserId == user).Select(m => m.PermissionSetId).SingleAsync()));
        access = Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, board.Id)));
        Assert.DoesNotContain(SharedPermission.Comment, access.Permissions);
        Assert.Contains(SharedPermission.Attach, access.Permissions);
    }
}
