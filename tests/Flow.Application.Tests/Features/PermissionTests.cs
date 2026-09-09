using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangePasswordCommand;
using Flow.Application.Features.Users.Commands.UserChangeRoleCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Матрицы прав docs/TZ_user_roles.md — по тесту на строку (#18). Owner из TestMediatorFactory уже в репозитории.</summary>
public class PermissionTests
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

    private static async Task<Guid> CreateBoardAsync(IMediator mediator) =>
        (await mediator.Send(new BoardCreateCommand(Owner, "Flow", "FLW"), CancellationToken.None)).Response!.Id;

    private static async Task<Guid> CreateTaskAsync(IMediator mediator, Guid actor, Guid boardId) =>
        (await mediator.Send(new TaskCreateCommand(actor, boardId, "Task", null, null), CancellationToken.None))!.Id;

    private static Task<ForbiddenException> Forbidden(Func<Task> action) => Assert.ThrowsAsync<ForbiddenException>(action);

    // ---- actor ----

    [Fact]
    public async Task Unknown_Actor_Should_Be_Unauthorized()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<UnauthorizedActorException>(() =>
            mediator.Send(new BoardCreateCommand(Guid.NewGuid(), "Flow", "FLW"), CancellationToken.None));
    }

    [Fact]
    public async Task Deactivated_Actor_Should_Be_Unauthorized()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        (await users.GetByIdAsync(admin, CancellationToken.None))!.Deactivate();

        await Assert.ThrowsAsync<UnauthorizedActorException>(() =>
            mediator.Send(new BoardCreateCommand(admin, "Flow", "FLW"), CancellationToken.None));
    }

    // ---- проекты ----

    [Theory]
    [InlineData(UserRole.Reader)]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Developer)]
    public async Task Below_Admin_Cannot_Manage_Boards(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);

        await Forbidden(() => mediator.Send(new BoardCreateCommand(actor, "Other", "OTH"), CancellationToken.None));
        await Forbidden(() => mediator.Send(new BoardDeleteCommand(actor, boardId), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_Can_Manage_Boards()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");

        var created = await mediator.Send(new BoardCreateCommand(admin, "Flow", "FLW"), CancellationToken.None);
        var deleted = await mediator.Send(new BoardDeleteCommand(admin, created.Response!.Id), CancellationToken.None);

        Assert.True(deleted);
    }

    // ---- задачи ----

    [Fact]
    public async Task Reader_Cannot_Create_Task_Member_Can_And_Becomes_Creator()
    {
        var (mediator, _, tasks, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);

        await Forbidden(() => mediator.Send(new TaskCreateCommand(reader, boardId, "T", null, null), CancellationToken.None));

        var task = await mediator.Send(new TaskCreateCommand(member, boardId, "T", null, null), CancellationToken.None);
        // CreatedById появится в TaskResponse в #19; пока проверяем по репозиторию.
        Assert.Equal(member, (await tasks.GetByIdAsync(task!.Id, CancellationToken.None))!.CreatedById);
    }

    [Fact]
    public async Task Member_Can_Edit_Own_Task_But_Not_Others()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var other = AddUser(users, UserRole.Member, "other");
        var boardId = await CreateBoardAsync(mediator);
        var own = await CreateTaskAsync(mediator, member, boardId);
        var foreign = await CreateTaskAsync(mediator, other, boardId);

        var updated = await mediator.Send(new TaskUpdateCommand(member, own, "Renamed", null, null), CancellationToken.None);
        Assert.Equal("Renamed", updated.Response!.Title);

        await Forbidden(() => mediator.Send(new TaskUpdateCommand(member, foreign, "Renamed", null, null), CancellationToken.None));
        await Forbidden(() => mediator.Send(new TaskDeleteCommand(member, foreign), CancellationToken.None));
    }

    [Fact]
    public async Task Member_Assigned_By_Developer_Can_Edit_That_Task()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var developer = AddUser(users, UserRole.Developer, "dev");
        var boardId = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, developer, boardId);
        await mediator.Send(new TaskAssignCommand(developer, task, member), CancellationToken.None);

        var updated = await mediator.Send(new TaskUpdateCommand(member, task, "By assignee", null, null), CancellationToken.None);

        Assert.Equal("By assignee", updated.Response!.Title);
    }

    [Fact]
    public async Task Member_Can_Assign_Only_Self()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var other = AddUser(users, UserRole.Member, "other");
        var boardId = await CreateBoardAsync(mediator);
        var own = await CreateTaskAsync(mediator, member, boardId);

        var assigned = await mediator.Send(new TaskAssignCommand(member, own, member), CancellationToken.None);
        Assert.Equal(member, assigned.Response!.AssigneeId);

        await Forbidden(() => mediator.Send(new TaskAssignCommand(member, own, other), CancellationToken.None));

        var unassigned = await mediator.Send(new TaskAssignCommand(member, own, null), CancellationToken.None);
        Assert.Null(unassigned.Response!.AssigneeId);
    }

    [Fact]
    public async Task Developer_Can_Edit_And_Assign_Any_Task()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var member = AddUser(users, UserRole.Member, "member");
        var boardId = await CreateBoardAsync(mediator);
        var task = await CreateTaskAsync(mediator, member, boardId);

        var assigned = await mediator.Send(new TaskAssignCommand(developer, task, member), CancellationToken.None);
        Assert.Equal(member, assigned.Response!.AssigneeId);

        Assert.True(await mediator.Send(new TaskDeleteCommand(developer, task), CancellationToken.None));
    }

    // ---- люди ----

    [Fact]
    public async Task Developer_Cannot_Create_Users_Admin_Can_Create_Member_But_Not_Admin()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var developer = AddUser(users, UserRole.Developer, "dev");
        var admin = AddUser(users, UserRole.Admin, "admin");

        await Forbidden(() => mediator.Send(new UserCreateCommand(developer, "n1", "n1@example.com", "A", "B", "correct horse battery"), CancellationToken.None));

        var created = await mediator.Send(new UserCreateCommand(admin, "n2", "n2@example.com", "A", "B", "correct horse battery", UserRole.Developer), CancellationToken.None);
        Assert.Equal(UserRole.Developer, (await users.GetByIdAsync(created.Response!.Id, CancellationToken.None))!.Role);

        await Forbidden(() => mediator.Send(new UserCreateCommand(admin, "n3", "n3@example.com", "A", "B", "correct horse battery", UserRole.Admin), CancellationToken.None));
        await Forbidden(() => mediator.Send(new UserCreateCommand(admin, "n4", "n4@example.com", "A", "B", "correct horse battery", UserRole.Owner), CancellationToken.None));
    }

    [Fact]
    public async Task Owner_Can_Create_Admin()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();

        var created = await mediator.Send(new UserCreateCommand(Owner, "n5", "n5@example.com", "A", "B", "correct horse battery", UserRole.Admin), CancellationToken.None);

        Assert.Equal(UserRole.Admin, (await users.GetByIdAsync(created.Response!.Id, CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task Profile_Self_For_Everyone_Others_For_Admin()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");
        var member = AddUser(users, UserRole.Member, "member");
        var admin = AddUser(users, UserRole.Admin, "admin");

        var self = await mediator.Send(new UserUpdateProfileCommand(reader, reader, "Новое", null, null, null, null, null), CancellationToken.None);
        Assert.Equal("Новое", self.Response!.FirstName);

        await Forbidden(() => mediator.Send(new UserUpdateProfileCommand(member, reader, "X", null, null, null, null, null), CancellationToken.None));

        var byAdmin = await mediator.Send(new UserUpdateProfileCommand(admin, reader, "Админ", null, null, null, null, null), CancellationToken.None);
        Assert.Equal("Админ", byAdmin.Response!.FirstName);
    }

    [Fact]
    public async Task Credentials_Of_Others_Only_For_Owner()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var admin = AddUser(users, UserRole.Admin, "admin");

        await Forbidden(() => mediator.Send(new UserChangeEmailCommand(admin, member, "new@example.com"), CancellationToken.None));
        await Forbidden(() => mediator.Send(new UserChangePasswordCommand(admin, member, null, "another strong password"), CancellationToken.None));

        var byOwner = await mediator.Send(new UserChangeEmailCommand(Owner, member, "new@example.com"), CancellationToken.None);
        Assert.Equal("new@example.com", byOwner.Response!.Email);

        var reset = await mediator.Send(new UserChangePasswordCommand(Owner, member, null, "another strong password"), CancellationToken.None);
        Assert.NotNull(reset.Response);
    }

    [Fact]
    public async Task Own_Password_Requires_Current()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new UserChangePasswordCommand(member, member, null, "another strong password"), CancellationToken.None));

        var ok = await mediator.Send(new UserChangePasswordCommand(member, member, "old", "another strong password"), CancellationToken.None);
        Assert.NotNull(ok.Response);
    }

    [Fact]
    public async Task Deactivate_Only_For_Owner()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var member = AddUser(users, UserRole.Member, "member");

        await Forbidden(() => mediator.Send(new UserDeactivateCommand(admin, member), CancellationToken.None));

        var result = await mediator.Send(new UserDeactivateCommand(Owner, member), CancellationToken.None);
        Assert.False(result.Response!.IsActive);
    }

    // ---- роли ----

    [Fact]
    public async Task Admin_Changes_Roles_Below_Admin_Only()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var otherAdmin = AddUser(users, UserRole.Admin, "admin2");
        var member = AddUser(users, UserRole.Member, "member");

        var promoted = await mediator.Send(new UserChangeRoleCommand(admin, member, UserRole.Developer), CancellationToken.None);
        Assert.Equal(UserRole.Developer, (await users.GetByIdAsync(member, CancellationToken.None))!.Role);
        Assert.NotNull(promoted.Response);

        await Forbidden(() => mediator.Send(new UserChangeRoleCommand(admin, member, UserRole.Admin), CancellationToken.None));
        await Forbidden(() => mediator.Send(new UserChangeRoleCommand(admin, otherAdmin, UserRole.Member), CancellationToken.None));
        await Forbidden(() => mediator.Send(new UserChangeRoleCommand(admin, Owner, UserRole.Member), CancellationToken.None));
    }

    [Fact]
    public async Task Member_Cannot_Change_Roles_And_Nobody_Promotes_Self()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var admin = AddUser(users, UserRole.Admin, "admin");

        await Forbidden(() => mediator.Send(new UserChangeRoleCommand(member, member, UserRole.Developer), CancellationToken.None));
        await Forbidden(() => mediator.Send(new UserChangeRoleCommand(admin, admin, UserRole.Owner), CancellationToken.None));
    }

    [Fact]
    public async Task Owner_Can_Grant_Owner_And_Admin_Can_Demote_Self()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");

        await mediator.Send(new UserChangeRoleCommand(Owner, admin, UserRole.Owner), CancellationToken.None);
        Assert.Equal(UserRole.Owner, (await users.GetByIdAsync(admin, CancellationToken.None))!.Role);

        await mediator.Send(new UserChangeRoleCommand(admin, admin, UserRole.Admin), CancellationToken.None);
        Assert.Equal(UserRole.Admin, (await users.GetByIdAsync(admin, CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task Last_Owner_Cannot_Be_Demoted()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new UserChangeRoleCommand(Owner, Owner, UserRole.Admin), CancellationToken.None));
        Assert.Equal(UserRole.Owner, (await users.GetByIdAsync(Owner, CancellationToken.None))!.Role);

        var second = AddUser(users, UserRole.Owner, "owner2");
        await mediator.Send(new UserChangeRoleCommand(second, Owner, UserRole.Admin), CancellationToken.None);
        Assert.Equal(UserRole.Admin, (await users.GetByIdAsync(Owner, CancellationToken.None))!.Role);
    }

    [Fact]
    public async Task Owner_Cannot_Be_Deactivated_Even_By_Owner()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var second = AddUser(users, UserRole.Owner, "owner2");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new UserDeactivateCommand(Owner, second), CancellationToken.None));
    }
}
