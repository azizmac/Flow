using Flow.Application.Exceptions;
using Flow.Application.Features.Attachments.Commands.AttachmentUploadCommand;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDefaultRoleSetCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberRemoveCommand;
using Flow.Application.Features.Boards.Commands.BoardMemberSetCommand;
using Flow.Application.Features.Boards.Commands.BoardRenameCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Queries.BoardMembersQuery;
using Flow.Application.Features.Boards.Queries.BoardMyAccessQuery;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using MediatR;
using Xunit;
using SharedPermission = Flow.Shared.Contracts.Boards.ProjectPermission;
using SharedRole = Flow.Shared.Contracts.Boards.ProjectRole;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Роли в проекте (docs/TZ_project_access.md, этап 4A): расчёт роли, таблица прав §2 — по тесту на строку,
/// команды участников §4. Главное свойство этапа — без участников и потолка всё как раньше — держат старые
/// PermissionTests, они не менялись.
/// </summary>
public class ProjectAccessTests
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

    private static async Task<Guid> CreateBoardAsync(IMediator mediator, string key = "FLW") =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Flow", key), CancellationToken.None)).Response!.Id;

    private static async Task<Guid> CreateTaskAsync(IMediator mediator, Guid actor, Guid boardId) =>
        (await mediator.Send(new TaskCreateCommand(actor, boardId, "Task", null, null), CancellationToken.None))!.Id;

    private static Task Join(IMediator mediator, Guid boardId, Guid userId, ProjectRole role) =>
        mediator.Send(new BoardMemberSetCommand(Owner, boardId, userId, role), CancellationToken.None);

    private static Task<ForbiddenException> Forbidden(Func<Task> action) => Assert.ThrowsAsync<ForbiddenException>(action);

    // ---- расчёт роли ----

    [Theory]
    [InlineData(UserRole.Reader, null, null, ProjectRole.Viewer)]
    [InlineData(UserRole.Member, null, null, ProjectRole.Member)]
    [InlineData(UserRole.Developer, null, null, ProjectRole.Developer)]
    [InlineData(UserRole.Admin, null, null, ProjectRole.Admin)]
    [InlineData(UserRole.Owner, null, null, ProjectRole.Admin)]
    [InlineData(UserRole.Developer, ProjectRole.Viewer, null, ProjectRole.Viewer)]
    [InlineData(UserRole.Reader, ProjectRole.Developer, null, ProjectRole.Viewer)]
    [InlineData(UserRole.Admin, ProjectRole.Viewer, null, ProjectRole.Admin)]
    [InlineData(UserRole.Developer, ProjectRole.Viewer, ProjectRole.Member, ProjectRole.Member)]
    [InlineData(UserRole.Reader, null, ProjectRole.Admin, ProjectRole.Admin)]
    [InlineData(UserRole.Developer, null, ProjectRole.Member, ProjectRole.Developer)]
    public void Effective_Should_TakeMaxOfCappedGlobalAndMembership(UserRole global, ProjectRole? ceiling, ProjectRole? member, ProjectRole expected) =>
        Assert.Equal(expected, ProjectRoles.Effective(global, ceiling, member));

    [Fact]
    public void PermissionsOf_Should_FollowTheLadder()
    {
        Assert.Equal([ProjectPermission.ViewProject], ProjectRoles.PermissionsOf(ProjectRole.Viewer));
        Assert.DoesNotContain(ProjectPermission.EditAnyTask, ProjectRoles.PermissionsOf(ProjectRole.Member));
        Assert.Contains(ProjectPermission.EditAnyTask, ProjectRoles.PermissionsOf(ProjectRole.Developer));
        Assert.DoesNotContain(ProjectPermission.ManageMembers, ProjectRoles.PermissionsOf(ProjectRole.Developer));
        Assert.Equal(Enum.GetValues<ProjectPermission>().Length, ProjectRoles.PermissionsOf(ProjectRole.Admin).Count);

        // Каждая ступень включает права предыдущей.
        var ladder = Enum.GetValues<ProjectRole>().Order().ToList();
        for (var i = 1; i < ladder.Count; i++)
            Assert.Superset(ProjectRoles.PermissionsOf(ladder[i - 1]).ToHashSet(), ProjectRoles.PermissionsOf(ladder[i]).ToHashSet());
    }

    // ---- таблица §2: участие повышает, потолок понижает ----

    [Fact]
    public async Task Reader_Joined_As_Member_Can_Create_And_Edit_Own_Task()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var boardId = await CreateBoardAsync(mediator);
        var otherBoardId = await CreateBoardAsync(mediator, "OTH");
        await Join(mediator, boardId, reader, ProjectRole.Member);

        var task = await CreateTaskAsync(mediator, reader, boardId);
        var updated = await mediator.Send(new TaskUpdateCommand(reader, task, "Своя", null, null), CancellationToken.None);

        Assert.Equal("Своя", updated.Response!.Title);
        // В соседнем проекте участия нет — там он по-прежнему читатель.
        await Forbidden(() => mediator.Send(new TaskCreateCommand(reader, otherBoardId, "T", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Member_Joined_As_Developer_Can_Edit_And_Assign_Any_Task()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var other = AddUser(users, UserRole.Member, "other");
        var boardId = await CreateBoardAsync(mediator);
        var foreign = await CreateTaskAsync(mediator, other, boardId);
        await Join(mediator, boardId, member, ProjectRole.Developer);

        await mediator.Send(new TaskUpdateCommand(member, foreign, "Чужая", null, null), CancellationToken.None);
        var assigned = await mediator.Send(new TaskAssignCommand(member, foreign, other), CancellationToken.None);

        Assert.Equal(other, assigned.Response!.AssigneeId);
    }

    [Fact]
    public async Task DefaultRole_Viewer_Makes_Project_ReadOnly_Except_Members_And_Global_Admins()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var joined = AddUser(users, UserRole.Developer, "joined");
        var admin = AddUser(users, UserRole.Admin, "admin");
        var boardId = await CreateBoardAsync(mediator);
        await mediator.Send(new BoardDefaultRoleSetCommand(Owner, boardId, ProjectRole.Viewer), CancellationToken.None);
        await Join(mediator, boardId, joined, ProjectRole.Member);

        await Forbidden(() => mediator.Send(new TaskCreateCommand(developer, boardId, "T", null, null), CancellationToken.None));
        Assert.NotEqual(Guid.Empty, await CreateTaskAsync(mediator, joined, boardId));
        Assert.NotEqual(Guid.Empty, await CreateTaskAsync(mediator, admin, boardId));
    }

    [Fact]
    public async Task DefaultRole_Admin_Is_Rejected()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var boardId = await CreateBoardAsync(mediator);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new BoardDefaultRoleSetCommand(Owner, boardId, ProjectRole.Admin), CancellationToken.None));
    }

    [Fact]
    public async Task Viewer_Cannot_Comment_Or_Attach_And_Loses_Own_Comment_Edit()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, Owner, boardId);
        var comment = (await mediator.Send(new TaskCommentAddCommand(member, task, "Было можно"), CancellationToken.None)).Response!;
        await mediator.Send(new BoardDefaultRoleSetCommand(Owner, boardId, ProjectRole.Viewer), CancellationToken.None);

        await Forbidden(() => mediator.Send(new TaskCommentAddCommand(member, task, "Уже нельзя"), CancellationToken.None));
        await Forbidden(() => mediator.Send(new TaskCommentEditCommand(member, comment.Id, "Правка"), CancellationToken.None));
        await Forbidden(() => mediator.Send(
            new AttachmentUploadCommand(member, task, "a.txt", 1, new MemoryStream([1])), CancellationToken.None));
    }

    [Fact]
    public async Task Project_Admin_By_Membership_Deletes_Foreign_Comments_And_Configures_But_Cannot_Delete_Project()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var lead = AddUser(users, UserRole.Developer, "lead");
        var author = AddUser(users, UserRole.Member, "author");
        var boardId = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, Owner, boardId);
        var comment = (await mediator.Send(new TaskCommentAddCommand(author, task, "Текст"), CancellationToken.None)).Response!;
        await Join(mediator, boardId, lead, ProjectRole.Admin);

        Assert.True(await mediator.Send(new TaskCommentDeleteCommand(lead, comment.Id), CancellationToken.None));
        Assert.NotNull(await mediator.Send(new BoardRenameCommand(lead, boardId, "Новое имя"), CancellationToken.None));
        Assert.NotNull(await mediator.Send(new TaskTypeCreateCommand(lead, boardId, "Инцидент", TaskTypeKind.Bug), CancellationToken.None));

        // Удаление уносит работу всех участников — нужен ещё и глобальный Admin+.
        await Forbidden(() => mediator.Send(new BoardDeleteCommand(lead, boardId), CancellationToken.None));
    }

    [Fact]
    public async Task Global_Admin_Without_Membership_Is_Project_Admin()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);

        var result = await mediator.Send(new BoardMemberSetCommand(admin, boardId, member, ProjectRole.Developer), CancellationToken.None);

        Assert.Equal(SharedRole.Developer, Assert.Single(result.Response!.Members).Role);
    }

    // ---- участники §4 ----

    [Theory]
    [InlineData(UserRole.Reader)]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Developer)]
    public async Task Below_Project_Admin_Cannot_Manage_Members_Or_DefaultRole(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var target = AddUser(users, UserRole.Member, "target");
        var boardId = await CreateBoardAsync(mediator);
        await Join(mediator, boardId, target, ProjectRole.Developer);

        await Forbidden(() => mediator.Send(new BoardMemberSetCommand(actor, boardId, target, ProjectRole.Member), CancellationToken.None));
        await Forbidden(() => mediator.Send(new BoardMemberRemoveCommand(actor, boardId, target), CancellationToken.None));
        await Forbidden(() => mediator.Send(new BoardDefaultRoleSetCommand(actor, boardId, ProjectRole.Viewer), CancellationToken.None));
    }

    [Fact]
    public async Task MemberSet_Should_Add_Then_ChangeRole_And_Remove_Returns_To_Default()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);

        var added = await mediator.Send(new BoardMemberSetCommand(Owner, boardId, member, ProjectRole.Developer), CancellationToken.None);
        var changed = await mediator.Send(new BoardMemberSetCommand(Owner, boardId, member, ProjectRole.Admin), CancellationToken.None);
        var removed = await mediator.Send(new BoardMemberRemoveCommand(Owner, boardId, member), CancellationToken.None);

        var entry = Assert.Single(added.Response!.Members);
        Assert.Equal((member, SharedRole.Developer, Owner), (entry.UserId, entry.Role, entry.AddedById));
        Assert.Equal(SharedRole.Admin, Assert.Single(changed.Response!.Members).Role);
        Assert.Empty(removed.Response!.Members);

        var access = Assert.Single(await mediator.Send(new BoardMyAccessQuery(member, boardId), CancellationToken.None));
        Assert.Equal(SharedRole.Member, access.Role);
    }

    [Fact]
    public async Task MemberSet_Should_Reject_Unknown_And_Deactivated_Users()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var gone = AddUser(users, UserRole.Member, "gone");
        (await users.GetByIdAsync(gone, CancellationToken.None))!.Deactivate();
        var boardId = await CreateBoardAsync(mediator);

        var unknown = await mediator.Send(new BoardMemberSetCommand(Owner, boardId, Guid.NewGuid(), ProjectRole.Member), CancellationToken.None);
        var deactivated = await mediator.Send(new BoardMemberSetCommand(Owner, boardId, gone, ProjectRole.Member), CancellationToken.None);

        Assert.NotNull(unknown.ValidationError);
        Assert.NotNull(deactivated.ValidationError);
    }

    [Fact]
    public async Task Member_Commands_Should_Return_NotFound()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);

        Assert.True((await mediator.Send(new BoardMemberSetCommand(Owner, Guid.NewGuid(), member, ProjectRole.Member), CancellationToken.None)).IsNotFound);
        Assert.True((await mediator.Send(new BoardMemberRemoveCommand(Owner, boardId, member), CancellationToken.None)).IsNotFound);
        Assert.Null(await mediator.Send(new BoardDefaultRoleSetCommand(Owner, Guid.NewGuid(), null), CancellationToken.None));
        Assert.Null(await mediator.Send(new BoardMembersQuery(Owner, Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Members_Query_Should_Carry_DefaultRole_And_Members_For_Any_Role()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);
        await Join(mediator, boardId, member, ProjectRole.Developer);
        await mediator.Send(new BoardDefaultRoleSetCommand(Owner, boardId, ProjectRole.Member), CancellationToken.None);

        var result = (await mediator.Send(new BoardMembersQuery(reader, boardId), CancellationToken.None))!;

        Assert.Equal(SharedRole.Member, result.DefaultRole);
        Assert.Equal(member, Assert.Single(result.Members).UserId);
    }

    [Fact]
    public async Task MyAccess_Should_Cover_All_Boards_With_Permissions()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var first = await CreateBoardAsync(mediator);
        var second = await CreateBoardAsync(mediator, "SEC");
        await Join(mediator, second, member, ProjectRole.Admin);

        var access = await mediator.Send(new BoardMyAccessQuery(member), CancellationToken.None);

        var one = access.Single(a => a.BoardId == first);
        var two = access.Single(a => a.BoardId == second);
        Assert.Equal(SharedRole.Member, one.Role);
        Assert.DoesNotContain(SharedPermission.EditAnyTask, one.Permissions);
        Assert.Equal(SharedRole.Admin, two.Role);
        Assert.Contains(SharedPermission.ManageMembers, two.Permissions);
    }
}
