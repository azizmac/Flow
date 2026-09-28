using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardVisibilitySetCommand;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Groups;
using Flow.Application.Features.PermissionSets;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Security;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using MediatR;
using Xunit;
using SharedPermission = Flow.Shared.Contracts.Boards.ProjectPermission;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Наборы прав (docs/TZ_project_access.md §7, этап 4E): встроенные — роли, свой набор — клон с базовой ролью; права —
/// объединение источников (роль по умолчанию, участие, группы); правит Owner; удаление набора возвращает права роли.
/// </summary>
public class PermissionSetFeatureTests
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

    [Fact]
    public void Resolve_Unions_Sources_And_Keeps_The_Ladder_Role()
    {
        var reviewer = PermissionSet.Create("Ревьюер", null, ProjectRole.Viewer, [ProjectPermission.Comment]);
        var sets = new Dictionary<Guid, PermissionSet> { [reviewer.Id] = reviewer };
        var boardId = Guid.NewGuid();

        var viaSet = ProjectRoles.Resolve(UserRole.Reader, new(boardId, BoardVisibility.Private, null, [new(ProjectRole.Viewer, reviewer.Id)]), sets);
        Assert.Equal(ProjectRole.Viewer, viaSet.Role);
        Assert.True(viaSet.Has(ProjectPermission.Comment));
        Assert.False(viaSet.Has(ProjectPermission.CreateTask));

        // В открытом проекте набор добавляет к правам роли по умолчанию, но не отнимает их.
        var open = ProjectRoles.Resolve(UserRole.Member, new(boardId, BoardVisibility.Open, null, [new(ProjectRole.Viewer, reviewer.Id)]), sets);
        Assert.True(open.Has(ProjectPermission.CreateTask) && open.Has(ProjectPermission.Comment));
        Assert.Equal(ProjectRole.Member, open.Role);

        // Удалённый набор — права базовой роли.
        var gone = ProjectRoles.Resolve(UserRole.Reader, new(boardId, BoardVisibility.Private, null, [new(ProjectRole.Viewer, Guid.NewGuid())]), sets);
        Assert.Equal([ProjectPermission.ViewProject], gone.Permissions);
        Assert.Equal(ProjectRole.Admin, ProjectRoles.Resolve(UserRole.Admin, new(boardId, BoardVisibility.Private, null, []), sets).Role);
    }

    [Fact]
    public async Task Custom_Set_Given_To_Member_And_Group_Changes_Rights()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var boardId = (await mediator.Send(new BoardCreateCommand(Owner, "Проект", "PSET"), CancellationToken.None)).Response!.Id;
        await mediator.Send(new BoardMemberSetCommand(Owner, boardId, Owner, ProjectRole.Admin), CancellationToken.None);
        await mediator.Send(new BoardVisibilitySetCommand(Owner, boardId, BoardVisibility.Private), CancellationToken.None);

        var creator = await mediator.Send(new PermissionSetCreateCommand(Owner, "Автор задач", "создаёт, но не правит чужое",
            ProjectRole.Viewer, [ProjectPermission.CreateTask, ProjectPermission.EditOwnTask]), CancellationToken.None);
        Assert.Contains(SharedPermission.ViewProject, creator.Permissions);
        var list = await mediator.Send(new PermissionSetListQuery(reader), CancellationToken.None);
        Assert.Equal(5, list.Count);
        Assert.Equal(4, list.Count(s => s.IsBuiltIn));

        var result = await mediator.Send(new BoardMemberSetCommand(Owner, boardId, reader, ProjectRole.Admin, creator.Id), CancellationToken.None);
        var member = result.Response!.Members.Single(m => m.UserId == reader);
        Assert.Equal((SharedRole.Viewer, creator.Id), (member.Role, member.PermissionSetId)); // роль — базовая роль набора
        Assert.NotNull(await mediator.Send(new TaskCreateCommand(reader, boardId, "Можно", null, null), CancellationToken.None));
        var access = Assert.Single(await mediator.Send(new BoardMyAccessQuery(reader, boardId), CancellationToken.None));
        Assert.DoesNotContain(SharedPermission.EditAnyTask, access.Permissions);

        // Встроенный Id — это просто роль.
        var builtIn = list.Single(s => s.IsBuiltIn && s.BaseRole == SharedRole.Developer).Id;
        member = (await mediator.Send(new BoardMemberSetCommand(Owner, boardId, reader, ProjectRole.Viewer, builtIn), CancellationToken.None)).Response!
            .Members.Single(m => m.UserId == reader);
        Assert.Equal((SharedRole.Developer, (Guid?)null), (member.Role, member.PermissionSetId));

        // Группа с набором; удаление набора — права базовой роли.
        var dev = AddUser(users, UserRole.Developer, "dev");
        var group = await mediator.Send(new GroupCreateCommand(Owner, "Авторы", null), CancellationToken.None);
        await mediator.Send(new GroupMemberSetCommand(Owner, group.Id, dev, true), CancellationToken.None);
        await mediator.Send(new BoardGroupSetCommand(Owner, boardId, group.Id, ProjectRole.Viewer, creator.Id), CancellationToken.None);
        Assert.NotNull(await mediator.Send(new TaskCreateCommand(dev, boardId, "Через группу", null, null), CancellationToken.None));
        Assert.True(await mediator.Send(new PermissionSetDeleteCommand(Owner, creator.Id), CancellationToken.None));
        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new TaskCreateCommand(dev, boardId, "Уже нельзя", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Only_Owner_Edits_Sets_And_Built_Ins_Are_Fixed()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var builtIn = (await mediator.Send(new PermissionSetListQuery(admin), CancellationToken.None)).First(s => s.IsBuiltIn).Id;

        await Assert.ThrowsAsync<ForbiddenException>(() => mediator.Send(new PermissionSetCreateCommand(admin, "Свой", null, ProjectRole.Member, []), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new PermissionSetUpdateCommand(Owner, builtIn, "X", null, []), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new PermissionSetDeleteCommand(Owner, builtIn), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new PermissionSetCreateCommand(Owner, "разработчик", null, ProjectRole.Member, []), CancellationToken.None));

        var set = await mediator.Send(new PermissionSetCreateCommand(Owner, "Свой", null, ProjectRole.Member, [ProjectPermission.Comment]), CancellationToken.None);
        var updated = await mediator.Send(new PermissionSetUpdateCommand(Owner, set.Id, "Свой+", "описание", [ProjectPermission.Comment, ProjectPermission.Attach]), CancellationToken.None);
        Assert.Equal(["Свой+", "описание"], [updated!.Name, updated.Description]);
        Assert.Equal(3, updated.Permissions.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new BoardMemberSetCommand(Owner, Guid.NewGuid(), admin, ProjectRole.Member, Guid.NewGuid()), CancellationToken.None));
    }
}
