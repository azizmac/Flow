using Flow.Application.Exceptions;
using Flow.Application.Features.About.Queries.AboutQuery;
using Flow.Application.Features.Attachments;
using Flow.Application.Features.Users.Commands.UserUpdatePreferencesCommand;
using Flow.Application.Features.Users.Queries.UserGetPreferencesQuery;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Users;
using Xunit;
using SidebarMode = Flow.Shared.Contracts.Users.SidebarMode;
using StartPage = Flow.Shared.Contracts.Users.StartPage;
using UserRole = Flow.Domain.Entities.UserRole;

namespace Flow.Application.Tests.Features;

/// <summary>Личные настройки (GET/PATCH /users/me/preferences) и «О системе» (GET /about).</summary>
public class PreferencesFeatureTests
{
    private static Guid AddUser(FakeUserRepository users, UserRole role, string username)
    {
        var user = User.Create(username, $"{username}@example.com", "Test", "User");
        user.ChangeRole(role);
        users.Add(user);
        return user.Id;
    }

    [Fact]
    public async Task Get_Should_ReturnDefaults_And_AllowedPageSizes()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var preferences = await mediator.Send(new UserGetPreferencesQuery(TestMediatorFactory.OwnerId), CancellationToken.None);

        Assert.Equal(SidebarMode.Auto, preferences.SidebarMode);
        Assert.Equal(StartPage.Projects, preferences.StartPage);
        Assert.Equal(100, preferences.TasksPageSize);
        Assert.Equal([10, 25, 50, 100], preferences.AllowedTasksPageSizes);
    }

    [Fact]
    public async Task Update_Should_ChangeOnlyGivenFields_OfActorOnly()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var other = AddUser(users, UserRole.Member, "other");

        await mediator.Send(new UserUpdatePreferencesCommand(TestMediatorFactory.OwnerId, SidebarMode.Collapsed, null, null), CancellationToken.None);
        var updated = await mediator.Send(new UserUpdatePreferencesCommand(TestMediatorFactory.OwnerId, null, StartPage.MyTasks, 25), CancellationToken.None);

        Assert.Equal(SidebarMode.Collapsed, updated.SidebarMode);
        Assert.Equal(StartPage.MyTasks, updated.StartPage);
        Assert.Equal(25, updated.TasksPageSize);

        var untouched = await mediator.Send(new UserGetPreferencesQuery(other), CancellationToken.None);
        Assert.Equal(SidebarMode.Auto, untouched.SidebarMode);
        Assert.Equal(100, untouched.TasksPageSize);
    }

    /// <summary>Строки в матрице прав у настроек нет: они личные, менять свои может и Reader.</summary>
    [Fact]
    public async Task Reader_Should_ChangeOwnPreferences()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var reader = AddUser(users, UserRole.Reader, "reader");

        var updated = await mediator.Send(new UserUpdatePreferencesCommand(reader, SidebarMode.Expanded, null, null), CancellationToken.None);

        Assert.Equal(SidebarMode.Expanded, updated.SidebarMode);
    }

    [Fact]
    public async Task Update_Should_RejectPageSizeOutsideAllowed()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new UserUpdatePreferencesCommand(TestMediatorFactory.OwnerId, null, null, 30), CancellationToken.None));
    }

    [Fact]
    public async Task Deactivated_Actor_Should_Be_Unauthorized()
    {
        var (mediator, _, _, users) = TestMediatorFactory.Create();
        var member = AddUser(users, UserRole.Member, "gone");
        (await users.GetByIdAsync(member, CancellationToken.None))!.Deactivate();

        await Assert.ThrowsAsync<UnauthorizedActorException>(() =>
            mediator.Send(new UserGetPreferencesQuery(member), CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedActorException>(() =>
            mediator.Send(new UserUpdatePreferencesCommand(member, SidebarMode.Expanded, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task About_Should_ReflectSearchSwitches_And_AttachmentLimits()
    {
        var (mediator, options, _, _) = TestMediatorFactory.CreateWithSearch();
        var limits = new AttachmentOptions();
        options.Embeddings.Vision.Enabled = true;

        var about = await mediator.Send(new AboutQuery(), CancellationToken.None);

        Assert.True(about.Modules.Search);
        Assert.True(about.Modules.SmartSearch);
        Assert.True(about.Modules.ImageSearch);
        Assert.False(about.Modules.Rerank);
        Assert.Equal(limits.MaxFileBytes, about.Limits.MaxFileBytes);
        Assert.Equal(limits.MaxPerTask, about.Limits.MaxFilesPerTask);
        Assert.False(string.IsNullOrWhiteSpace(about.Version));

        // Выключатель «без моделей» гасит и картинки, и вторую ступень — как в самом поиске.
        options.Embeddings.Enabled = false;
        options.Rerank.Enabled = true;
        about = await mediator.Send(new AboutQuery(), CancellationToken.None);

        Assert.True(about.Modules.Search);
        Assert.False(about.Modules.SmartSearch);
        Assert.False(about.Modules.ImageSearch);
        Assert.False(about.Modules.Rerank);
    }
}
