using Flow.Application.Features.Dashboards;
using Flow.Application.Features.Filters;
using Flow.Application.Features.Tasks.Restructure;
using Flow.Application.Features.Tasks.Recurrence;
using Flow.Application.Features.GitIntegration;
using Flow.Application.Exceptions;
using Flow.Application.Features.Boards.Commands.BoardCreateCommand;
using Flow.Application.Features.Boards.Commands.BoardDeleteCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;
using Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;
using Flow.Application.Features.Search.Commands.ReindexCommand;
using Flow.Application.Features.Milestones;
using Flow.Application.Features.Sprints.Commands.SprintCreateCommand;
using Flow.Application.Features.Search.Queries.SearchStatusQuery;
using Flow.Application.Features.Tasks.Commands.TaskAssignCommand;
using Flow.Application.Features.Tasks.Commands.TaskCreateCommand;
using Flow.Application.Features.Tasks.Commands.TaskDeleteCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetEstimateCommand;
using Flow.Application.Features.Tasks.Commands.TaskSetScheduleCommand;
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

    // ---- типы задач (docs/TZ_task_model.md §1) ----

    [Theory]
    [InlineData(UserRole.Reader)]
    [InlineData(UserRole.Member)]
    [InlineData(UserRole.Developer)]
    public async Task Below_Admin_Cannot_Manage_Task_Types(UserRole role)
    {
        var (mediator, boards, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);
        var typeId = (await boards.GetByIdAsync(boardId, CancellationToken.None))!.TaskTypes.First().Id;

        await Forbidden(() => mediator.Send(new TaskTypeCreateCommand(actor, boardId, "Инцидент", TaskTypeKind.Bug), CancellationToken.None));
        await Forbidden(() => mediator.Send(new TaskTypeUpdateCommand(actor, boardId, typeId, Name: "X"), CancellationToken.None));
    }

    [Fact]
    public async Task Admin_Can_Manage_Task_Types()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var boardId = await CreateBoardAsync(mediator);

        var board = await mediator.Send(new TaskTypeCreateCommand(admin, boardId, "Инцидент", TaskTypeKind.Bug), CancellationToken.None);

        Assert.Contains(board!.TaskTypes, t => t.Name == "Инцидент");
    }

    // ---- спринты (docs/TZ_task_views.md §2): ManageSprints — Developer и выше ----

    [Theory]
    [InlineData(UserRole.Reader)]
    [InlineData(UserRole.Member)]
    public async Task Below_Developer_Cannot_Manage_Sprints(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);

        await Forbidden(() => mediator.Send(new SprintCreateCommand(actor, boardId), CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Developer)]
    [InlineData(UserRole.Admin)]
    public async Task Developer_And_Above_Can_Manage_Sprints(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);

        Assert.Equal("Спринт 1", (await mediator.Send(new SprintCreateCommand(actor, boardId), CancellationToken.None))!.Name);
    }

    // ---- шаблоны задач (docs/TZ_workflow_config.md §5): ManageTaskTemplates — Developer и выше ----

    [Theory]
    [InlineData(UserRole.Reader, false)]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Developer, true)]
    [InlineData(UserRole.Admin, true)]
    public async Task Developer_And_Above_Manage_Task_Templates(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);
        var create = new Flow.Application.Features.TaskTemplates.TaskTemplateCreateCommand(actor, boardId,
            new Flow.Shared.Contracts.Tasks.SaveTaskTemplateRequest("Релиз", "Релиз {n}"));

        if (allowed)
            Assert.Equal("Релиз", (await mediator.Send(create, CancellationToken.None))!.Name);
        else
            await Forbidden(() => mediator.Send(create, CancellationToken.None));
    }

    // ---- вехи (docs/TZ_task_views.md §6): ManageMilestones — Developer и выше ----

    [Theory]
    [InlineData(UserRole.Reader)]
    [InlineData(UserRole.Member)]
    public async Task Below_Developer_Cannot_Manage_Milestones(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);

        await Forbidden(() => mediator.Send(new MilestoneCreateCommand(actor, boardId, "1.0"), CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Developer)]
    [InlineData(UserRole.Admin)]
    public async Task Developer_And_Above_Can_Manage_Milestones(UserRole role)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "user");
        var boardId = await CreateBoardAsync(mediator);

        Assert.Equal("1.0", (await mediator.Send(new MilestoneCreateCommand(actor, boardId, "1.0"), CancellationToken.None))!.Name);
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
    public async Task Planning_Fields_Follow_EditTask_Rights()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "member");
        var other = AddUser(users, UserRole.Member, "other");
        var reader = AddUser(users, UserRole.Reader, "reader");
        var boardId = await CreateBoardAsync(mediator);
        var own = await CreateTaskAsync(mediator, member, boardId);
        var foreign = await CreateTaskAsync(mediator, other, boardId);

        var updated = await mediator.Send(new TaskUpdateCommand(member, own, null, null, null, Priority: TaskPriority.High), CancellationToken.None);
        Assert.Equal(Shared.Contracts.Tasks.TaskPriority.High, updated.Response!.Priority);
        Assert.NotNull((await mediator.Send(new TaskSetScheduleCommand(member, own, new DateOnly(2026, 10, 1), null), CancellationToken.None)).Response);
        Assert.NotNull((await mediator.Send(new TaskSetEstimateCommand(member, own, 3m, 60), CancellationToken.None)).Response);

        foreach (var actor in new[] { member, reader })
        {
            await Forbidden(() => mediator.Send(new TaskUpdateCommand(actor, foreign, null, null, null, Priority: TaskPriority.High), CancellationToken.None));
            await Forbidden(() => mediator.Send(new TaskSetScheduleCommand(actor, foreign, new DateOnly(2026, 10, 1), null), CancellationToken.None));
            await Forbidden(() => mediator.Send(new TaskSetEstimateCommand(actor, foreign, 3m, 60), CancellationToken.None));
        }
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

        var self = await mediator.Send(new UserUpdateProfileCommand(reader, reader, "Новое", null, null, null, null), CancellationToken.None);
        Assert.Equal("Новое", self.Response!.FirstName);

        await Forbidden(() => mediator.Send(new UserUpdateProfileCommand(member, reader, "X", null, null, null, null), CancellationToken.None));

        var byAdmin = await mediator.Send(new UserUpdateProfileCommand(admin, reader, "Админ", null, null, null, null), CancellationToken.None);
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

    // ---- поиск ----

    [Fact]
    public async Task Search_Status_Is_Admin_Only()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");
        var developer = AddUser(users, UserRole.Developer, "dev");

        Assert.NotNull(await mediator.Send(new SearchStatusQuery(admin), CancellationToken.None));
        await Forbidden(() => mediator.Send(new SearchStatusQuery(developer), CancellationToken.None));
    }

    [Fact]
    public async Task Reindex_Is_Owner_Only()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var admin = AddUser(users, UserRole.Admin, "admin");

        await mediator.Send(new ReindexCommand(Owner, null, null), CancellationToken.None);
        await Forbidden(() => mediator.Send(new ReindexCommand(admin, null, null), CancellationToken.None));
    }

    // ---- Сохранённые фильтры (docs/TZ_task_views.md §7) ----

    [Theory]
    [InlineData(UserRole.Owner, false)]
    [InlineData(UserRole.Member, false)]
    public async Task SavedFilter_Edit_Only_Author(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var filter = await mediator.Send(new SavedFilterCreateCommand(Owner, "Общий", "", true), CancellationToken.None);

        Task Edit() => mediator.Send(new SavedFilterUpdateCommand(actor, filter.Id, "X", null, null), CancellationToken.None);

        if (allowed) await Edit(); else await Forbidden(Edit);
        Assert.NotNull(await mediator.Send(new SavedFilterUpdateCommand(Owner, filter.Id, "Своё", null, null), CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task SavedFilter_Delete_Shared_Author_Or_Admin(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var filter = await mediator.Send(new SavedFilterCreateCommand(Owner, "Общий", "", true), CancellationToken.None);

        Task Delete() => mediator.Send(new SavedFilterDeleteCommand(actor, filter.Id), CancellationToken.None);

        if (allowed) await Delete(); else await Forbidden(Delete);
    }

    [Theory]
    [InlineData(UserRole.Owner, false)]
    [InlineData(UserRole.Member, false)]
    public async Task Dashboard_Edit_Only_Author(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var author = AddUser(users, UserRole.Member, "author");
        var dashboard = await mediator.Send(new DashboardCreateCommand(author, "Общий", true), CancellationToken.None);

        Task Edit() => mediator.Send(new DashboardUpdateCommand(actor, dashboard.Id, Name: "X"), CancellationToken.None);

        if (allowed) await Edit(); else await Forbidden(Edit);
        Assert.NotNull(await mediator.Send(new DashboardUpdateCommand(author, dashboard.Id, Name: "Своё"), CancellationToken.None));
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task Dashboard_Delete_Shared_Author_Or_Admin(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var dashboard = await mediator.Send(new DashboardCreateCommand(Owner, "Общий", true), CancellationToken.None);

        Task Delete() => mediator.Send(new DashboardDeleteCommand(actor, dashboard.Id), CancellationToken.None);

        if (allowed) await Delete(); else await Forbidden(Delete);
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Developer, true)]
    public async Task Merge_Needs_Edit_On_Both_Tasks(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var board = await CreateBoardAsync(mediator);
        var source = await CreateTaskAsync(mediator, Owner, board);
        var target = await CreateTaskAsync(mediator, Owner, board);

        Task Merge() => mediator.Send(new TaskMergeCommand(actor, source, target), CancellationToken.None);

        if (allowed) await Merge(); else await Forbidden(Merge);
    }

    [Theory]
    [InlineData(UserRole.Reader, false)]
    [InlineData(UserRole.Member, true)]
    public async Task Split_Needs_Edit_And_Create(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var board = await CreateBoardAsync(mediator);
        // Своя задача Member: создал сам — правка разрешена правом EditOwnTask.
        var source = await CreateTaskAsync(mediator, allowed ? actor : Owner, board);

        Task Split() => mediator.Send(new TaskSplitCommand(actor, source, [new Flow.Shared.Contracts.Tasks.SplitPart("Часть")]), CancellationToken.None);

        if (allowed) await Split(); else await Forbidden(Split);
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Developer, true)]
    public async Task Move_Needs_Edit_In_Source_And_Create_In_Target(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var board = await CreateBoardAsync(mediator);
        var target = (await mediator.Send(new BoardCreateCommand(Owner, "Цель", "DST"), CancellationToken.None)).Response!.Id;
        var task = await CreateTaskAsync(mediator, Owner, board);

        Task Move() => mediator.Send(new TaskMoveCommand(actor, task, target), CancellationToken.None);

        if (allowed) await Move(); else await Forbidden(Move);
    }

    [Theory]
    [InlineData(UserRole.Member, false)]
    [InlineData(UserRole.Developer, true)]
    public async Task Recurrence_Needs_Edit_On_The_Template(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var task = await CreateTaskAsync(mediator, Owner, await CreateBoardAsync(mediator));
        var rule = new Flow.Shared.Contracts.Tasks.TaskRecurrenceRequest(Flow.Shared.Contracts.Tasks.RecurrenceFrequency.Daily, 1, new DateOnly(2030, 1, 1));

        Task Set() => mediator.Send(new TaskRecurrenceSetCommand(actor, task, rule), CancellationToken.None);

        if (allowed) await Set(); else await Forbidden(Set);
    }

    [Theory]
    [InlineData(UserRole.Developer, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task Integrations_Are_Managed_By_Global_Admins(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");

        Task List() => mediator.Send(new GitHostConnectionListQuery(actor), CancellationToken.None);

        if (allowed) await List(); else await Forbidden(List);
    }

    [Theory]
    [InlineData(UserRole.Developer, false)]
    [InlineData(UserRole.Admin, true)]
    public async Task Repositories_Are_Bound_By_Project_Admins(UserRole role, bool allowed)
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var actor = AddUser(users, role, "actor");
        var board = await CreateBoardAsync(mediator);

        Task List() => mediator.Send(new GitBoardRepositoriesQuery(actor, board), CancellationToken.None);

        if (allowed) await List(); else await Forbidden(List);
    }
}
