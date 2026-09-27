using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Queries.BoardGetQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Участники и потолок роли на реальном Postgres (docs/TZ_project_access.md, этап 4A): составной ключ,
/// каскад при удалении проекта и запрос «потолок + участие», который считает роль для каждой команды.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ProjectAccessPersistenceTests(PostgresFixture db)
{
    private async Task<Guid> CreateUserAsync(string username) =>
        (await db.SendAsync(new UserCreateCommand(PostgresFixture.OwnerId, username, $"{username}@example.com", "Имя", "Фамилия",
            Password: "password123"))).Response!.Id;

    [Fact]
    public async Task Membership_And_DefaultRole_Should_Drive_Access()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Access", "ACC"))).Response!;
        var other = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Access other", "ACO"))).Response!;
        var user = await CreateUserAsync("acc.member");

        await db.SendAsync(new BoardDefaultRoleSetCommand(PostgresFixture.OwnerId, board.Id, ProjectRole.Viewer));
        var viewer = Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, board.Id)));
        Assert.Equal(SharedRole.Viewer, viewer.Role);

        await db.SendAsync(new BoardMemberSetCommand(PostgresFixture.OwnerId, board.Id, user, ProjectRole.Developer));
        await db.SendAsync(new BoardMemberSetCommand(PostgresFixture.OwnerId, board.Id, user, ProjectRole.Admin));

        var all = await db.SendAsync(new BoardMyAccessQuery(user));
        Assert.Equal(SharedRole.Admin, all.Single(a => a.BoardId == board.Id).Role);
        Assert.Equal(SharedRole.Member, all.Single(a => a.BoardId == other.Id).Role);
        Assert.Equal(1, await db.QueryAsync(ctx => ctx.BoardMembers.CountAsync(m => m.BoardId == board.Id)));
        Assert.Equal(SharedRole.Viewer, (await db.SendAsync(new BoardGetQuery(board.Id)))!.DefaultRole);

        await db.SendAsync(new BoardMemberRemoveCommand(PostgresFixture.OwnerId, board.Id, user));
        Assert.Equal(SharedRole.Viewer, Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, board.Id))).Role);
    }

    [Fact]
    public async Task DeleteBoard_Should_Cascade_Members()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(PostgresFixture.OwnerId, "Access delete", "ACD"))).Response!;
        var user = await CreateUserAsync("acd.member");
        await db.SendAsync(new BoardMemberSetCommand(PostgresFixture.OwnerId, board.Id, user, ProjectRole.Member));

        Assert.True(await db.SendAsync(new BoardDeleteCommand(PostgresFixture.OwnerId, board.Id)));

        Assert.Equal(0, await db.QueryAsync(ctx => ctx.BoardMembers.CountAsync(m => m.BoardId == board.Id)));
    }

    [Fact]
    public async Task MyAccess_For_Unknown_Board_Should_Be_Empty()
    {
        Assert.Empty(await db.SendAsync(new BoardMyAccessQuery(PostgresFixture.OwnerId, Guid.NewGuid())));
    }
}
