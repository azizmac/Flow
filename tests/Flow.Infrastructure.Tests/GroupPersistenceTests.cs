using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardListQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Groups;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Группы на Postgres (этап 4C): роль участия — MAX из прямой и групповых подзапросом в том же запросе, что считает
/// роль для каждой команды; приватный проект виден через группу; удаление группы каскадом уносит состав и роли.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GroupPersistenceTests(PostgresFixture db)
{
    private static readonly Guid Owner = PostgresFixture.OwnerId;

    [Fact]
    public async Task Group_Roles_Raise_Access_And_Open_Private_Projects()
    {
        var open = (await db.SendAsync(new BoardCreateCommand(Owner, "Группы открытый", "GRPO"))).Response!;
        var hidden = (await db.SendAsync(new BoardCreateCommand(Owner, "Группы закрытый", "GRPH"))).Response!;
        await db.SendAsync(new BoardMemberSetCommand(Owner, hidden.Id, Owner, ProjectRole.Admin));
        await db.SendAsync(new BoardVisibilitySetCommand(Owner, hidden.Id, BoardVisibility.Private));
        await db.SendAsync(new BoardDefaultRoleSetCommand(Owner, open.Id, ProjectRole.Viewer));
        var user = (await db.SendAsync(new UserCreateCommand(Owner, "grp.user", "grp.user@example.com", "Имя", "Фамилия", Password: "password123"))).Response!.Id;

        var a = await db.SendAsync(new GroupCreateCommand(Owner, "Группа А", null));
        var b = await db.SendAsync(new GroupCreateCommand(Owner, "Группа Б", null));
        await db.SendAsync(new GroupMemberSetCommand(Owner, a.Id, user, true));
        await db.SendAsync(new GroupMemberSetCommand(Owner, b.Id, user, true));
        await db.SendAsync(new BoardGroupSetCommand(Owner, open.Id, a.Id, ProjectRole.Member));
        await db.SendAsync(new BoardGroupSetCommand(Owner, open.Id, b.Id, ProjectRole.Developer));
        await db.SendAsync(new BoardGroupSetCommand(Owner, hidden.Id, a.Id, ProjectRole.Viewer));

        var access = await db.SendAsync(new BoardMyAccessQuery(user));
        Assert.Equal(SharedRole.Developer, access.Single(x => x.BoardId == open.Id).Role);
        Assert.Equal(SharedRole.Viewer, access.Single(x => x.BoardId == hidden.Id).Role);
        Assert.Contains(await db.SendAsync(new BoardListQuery(user)), x => x.Id == hidden.Id);

        // Прямое участие выше групп — берётся оно.
        await db.SendAsync(new BoardMemberSetCommand(Owner, hidden.Id, user, ProjectRole.Admin));
        Assert.Equal(SharedRole.Admin, Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, hidden.Id))).Role);

        Assert.True(await db.SendAsync(new GroupDeleteCommand(Owner, b.Id)));
        Assert.Equal(SharedRole.Member, Assert.Single(await db.SendAsync(new BoardMyAccessQuery(user, open.Id))).Role);
        Assert.False(await db.QueryAsync(ctx => ctx.BoardGroups.AnyAsync(g => g.GroupId == b.Id)));
        Assert.False(await db.QueryAsync(ctx => ctx.GroupMembers.AnyAsync(m => m.GroupId == b.Id)));
    }

    [Fact]
    public async Task Team_Filters_In_Fql_And_Is_Cleared_When_Group_Goes()
    {
        var board = (await db.SendAsync(new BoardCreateCommand(Owner, "Команды", "TEAMP"))).Response!;
        var a = (await db.SendAsync(new Flow.Application.Features.Tasks.Commands.TaskCreateCommand.TaskCreateCommand(Owner, board.Id, "С командой", null, null)))!;
        var b = (await db.SendAsync(new Flow.Application.Features.Tasks.Commands.TaskCreateCommand.TaskCreateCommand(Owner, board.Id, "Без команды", null, null)))!;
        var team = await db.SendAsync(new GroupCreateCommand(Owner, "Команда TEAMP", null, IsTeam: true));
        await db.SendAsync(new GroupMemberSetCommand(Owner, team.Id, Owner, true));
        await db.SendAsync(new TaskSetTeamCommand(Owner, a.Id, team.Id));

        async Task<List<Guid>> Find(string fql) =>
            (await db.SendAsync(new Flow.Application.Features.Tasks.Queries.TaskSearchQuery.TaskSearchQuery(Owner, board.Id, Fql: fql))).Items.Select(t => t.Id).ToList();

        Assert.Equal([a.Id], await Find("team = \"Команда TEAMP\""));
        Assert.Equal([a.Id], await Find("team in (myTeams())"));
        Assert.Equal([b.Id], await Find("team IS EMPTY"));

        Assert.True(await db.SendAsync(new GroupDeleteCommand(Owner, team.Id)));
        Assert.Null(await db.QueryAsync(ctx => ctx.Set<TaskItem>().Where(t => t.Id == a.Id).Select(t => t.TeamId).SingleAsync()));
    }
}
