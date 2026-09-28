using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardMembersQuery;
using Flow.Application.Features.Groups;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Queries.TaskListQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using MediatR;
using Xunit;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Группы и роли групп в проекте (docs/TZ_project_access.md, этап 4C): итоговая роль — максимум из роли по умолчанию,
/// прямого участия и групп; приватный проект виден через группу; справочник групп меняют только Admin+.
/// </summary>
public class GroupFeatureTests
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

    private static async Task<Guid> GroupWith(IMediator mediator, string name, params Guid[] members)
    {
        var group = await mediator.Send(new GroupCreateCommand(Owner, name, null), CancellationToken.None);
        foreach (var member in members)
            await mediator.Send(new GroupMemberSetCommand(Owner, group.Id, member, true), CancellationToken.None);
        return group.Id;
    }

    [Fact]
    public async Task Role_Is_The_Max_Of_Direct_And_Group_Roles()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var boardId = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "GRP"), CancellationToken.None)).Response!.Id;
        var backend = await GroupWith(mediator, "Бэкенд", reader);
        var design = await GroupWith(mediator, "Дизайн", reader);

        // Reader может только смотреть; группа «Дизайн» даёт Member — уже можно создавать задачи.
        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCreateCommand(reader, boardId, "X", null, null), CancellationToken.None));
        await mediator.Send(new BoardGroupSetCommand(Owner, boardId, design, ProjectRole.Member), CancellationToken.None);
        await mediator.Send(new BoardGroupSetCommand(Owner, boardId, backend, ProjectRole.Viewer), CancellationToken.None);
        Assert.NotNull(await mediator.Send(new TaskCreateCommand(reader, boardId, "Можно", null, null), CancellationToken.None));

        var members = (await mediator.Send(new BoardMembersQuery(Owner, boardId), CancellationToken.None))!;
        Assert.Equal(["Дизайн", "Бэкенд"], members.Groups!.Select(g => g.Name));
        Assert.Equal(SharedRole.Member, members.Groups![0].Role);
        Assert.Equal(1, members.Groups[0].MemberCount);

        // Человек ушёл из группы — роль снова по умолчанию.
        await mediator.Send(new GroupMemberSetCommand(Owner, design, reader, false), CancellationToken.None);
        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCreateCommand(reader, boardId, "Y", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Private_Project_Is_Visible_Through_A_Group_And_Hidden_After_Removal()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var dev = AddUser(users, UserRole.Developer, "dev");
        var boardId = (await mediator.Send(new BoardCreateCommand(Owner, "Секрет", "SEC"), CancellationToken.None)).Response!.Id;
        await mediator.Send(new TaskCreateCommand(Owner, boardId, "Тайна", null, null), CancellationToken.None);
        await mediator.Send(new BoardMemberSetCommand(Owner, boardId, Owner, ProjectRole.Admin), CancellationToken.None);
        await mediator.Send(new BoardVisibilitySetCommand(Owner, boardId, BoardVisibility.Private), CancellationToken.None);
        var team = await GroupWith(mediator, "Команда", dev);

        Assert.Empty(await mediator.Send(new TaskListQuery(dev, boardId), CancellationToken.None));
        await mediator.Send(new BoardGroupSetCommand(Owner, boardId, team, ProjectRole.Developer), CancellationToken.None);
        Assert.Single(await mediator.Send(new TaskListQuery(dev, boardId), CancellationToken.None));

        Assert.True((await mediator.Send(new BoardGroupRemoveCommand(Owner, boardId, team), CancellationToken.None)).Response is not null);
        Assert.Empty(await mediator.Send(new TaskListQuery(dev, boardId), CancellationToken.None));
        Assert.True((await mediator.Send(new BoardGroupRemoveCommand(Owner, boardId, team), CancellationToken.None)).IsNotFound);

        // Удалённая группа уносит и роли в проектах.
        await mediator.Send(new BoardGroupSetCommand(Owner, boardId, team, ProjectRole.Developer), CancellationToken.None);
        Assert.True(await mediator.Send(new GroupDeleteCommand(Owner, team), CancellationToken.None));
        Assert.Empty(await mediator.Send(new TaskListQuery(dev, boardId), CancellationToken.None));
    }

    [Fact]
    public async Task Groups_Are_Managed_By_Admins_And_Project_Roles_Not_Above_Own()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var dev = AddUser(users, UserRole.Developer, "dev");
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "OWN"), CancellationToken.None)).Response!.Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new GroupCreateCommand(dev, "Свои", null), CancellationToken.None));
        var group = await GroupWith(mediator, "Свои", member);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new GroupCreateCommand(Owner, "свои", null), CancellationToken.None));
        Assert.Single((await mediator.Send(new GroupListQuery(dev), CancellationToken.None)).Single().MemberIds);

        // Разработчик проекта группами не управляет вовсе; администратор проекта не выдаёт роль выше своей.
        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new BoardGroupSetCommand(dev, boardId, group, ProjectRole.Viewer), CancellationToken.None));
        Assert.Equal("Группа не найдена.", (await mediator.Send(new BoardGroupSetCommand(Owner, boardId, Guid.NewGuid(), ProjectRole.Viewer), CancellationToken.None)).ValidationError);

        var renamed = await mediator.Send(new GroupUpdateCommand(Owner, group, "Наши", "описание"), CancellationToken.None);
        Assert.Equal("Наши", renamed!.Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new GroupMemberSetCommand(Owner, group, Guid.NewGuid(), true), CancellationToken.None));
    }

    [Fact]
    public async Task Task_Team_Is_A_Team_Group_With_Journal()
    {
        var (mediator, _, _, _, activities, _, _) = TestMediatorFactory.CreateWithJournal();
        var boardId = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "TEAM"), CancellationToken.None)).Response!.Id;
        var task = (await mediator.Send(new TaskCreateCommand(Owner, boardId, "Задача", null, null), CancellationToken.None))!;
        var team = await mediator.Send(new GroupCreateCommand(Owner, "Бэкенд", null, IsTeam: true), CancellationToken.None);
        var plain = await mediator.Send(new GroupCreateCommand(Owner, "Все", null), CancellationToken.None);
        Assert.True(team.IsTeam);

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskSetTeamCommand(Owner, task.Id, plain.Id), CancellationToken.None));
        var set = await mediator.Send(new TaskSetTeamCommand(Owner, task.Id, team.Id), CancellationToken.None);
        Assert.Equal(team.Id, set.Response!.TeamId);
        Assert.Equal(team.Id.ToString(), Assert.Single(activities.ForTask(task.Id), a => a.Type == Flow.Domain.Entities.TaskActivityType.TeamChanged).NewValue);

        // Снятый флаг не выбивает команду из задачи: повтор — no-op, а новую такую назначить уже нельзя.
        await mediator.Send(new GroupUpdateCommand(Owner, team.Id, "Бэкенд", null, IsTeam: false), CancellationToken.None);
        Assert.Equal(team.Id, (await mediator.Send(new TaskSetTeamCommand(Owner, task.Id, team.Id), CancellationToken.None)).Response!.TeamId);
        Assert.Null((await mediator.Send(new TaskSetTeamCommand(Owner, task.Id, null), CancellationToken.None)).Response!.TeamId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new TaskSetTeamCommand(Owner, task.Id, team.Id), CancellationToken.None));
        Assert.True((await mediator.Send(new TaskSetTeamCommand(Owner, Guid.NewGuid(), null), CancellationToken.None)).IsNotFound);
    }
}
