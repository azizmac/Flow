using Flow.Application.Features.Users.Commands.UserActivateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Commands.UserRemoveLinkCommand;
using Flow.Application.Features.Users.Commands.UserSetLinkCommand;
using Flow.Application.Features.Users.Commands.UserUpdateProfileCommand;
using Flow.Application.Features.Users.Queries.UserGetByUsernameQuery;
using Flow.Application.Features.Users.Queries.UserGetQuery;
using Flow.Application.Features.Users.Queries.UserListQuery;
using Flow.Application.Features.Users.Queries.UserSearchQuery;
using Flow.Shared.Contracts.Users;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

public class UserFeatureTests
{
    private static async Task<UserResponse> CreateUserAsync(IMediator mediator, string username = "ilya", string email = "ilya@example.com")
    {
        var result = await mediator.Send(new UserCreateCommand(username, email, "Илья", "Моторин", "correct horse battery"), CancellationToken.None);
        Assert.False(result.IsConflict);
        return result.Response!;
    }

    [Fact]
    public async Task CreateUser_Should_NormalizeUsernameAndEmail()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var result = await mediator.Send(new UserCreateCommand(" Ilya ", " Ilya@Example.COM ", "Илья", "Моторин", "correct horse battery"), CancellationToken.None);

        Assert.False(result.IsConflict);
        Assert.Equal("ilya", result.Response!.Username);
        Assert.Equal("ilya@example.com", result.Response.Email);
        Assert.Equal("Илья Моторин", result.Response.FullName);
        Assert.True(result.Response.IsActive);
    }

    [Fact]
    public async Task CreateUser_Should_ReturnUsernameTaken_When_UsernameExistsInDifferentCase()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserCreateCommand("ILYA", "other@example.com", "A", "B", "correct horse battery"), CancellationToken.None);

        Assert.True(result.IsUsernameTaken);
        Assert.False(result.IsEmailTaken);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task CreateUser_Should_ReturnEmailTaken_When_EmailExists()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserCreateCommand("other", "ILYA@example.com", "A", "B", "correct horse battery"), CancellationToken.None);

        Assert.True(result.IsEmailTaken);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task CreateUser_Should_Throw_When_UsernameIsInvalid()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new UserCreateCommand("bad user!", "a@b.c", "A", "B", "correct horse battery"), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateProfile_Should_ChangeOnlyProvidedFields()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        var result = await mediator.Send(
            new UserUpdateProfileCommand(created.Id, null, "Иванов", "Backend", null, "+7 (999) 123-45-67", null),
            CancellationToken.None);

        var response = result.Response!;
        Assert.Equal("Илья", response.FirstName);
        Assert.Equal("Иванов", response.LastName);
        Assert.Equal("Backend", response.JobTitle);
        Assert.Null(response.Bio);
        Assert.Equal("+79991234567", response.PhoneNumber);
    }

    [Fact]
    public async Task UpdateProfile_Should_ClearField_When_EmptyString()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);
        await mediator.Send(new UserUpdateProfileCommand(created.Id, null, null, "Backend", null, null, null), CancellationToken.None);

        var result = await mediator.Send(new UserUpdateProfileCommand(created.Id, null, null, "", null, null, null), CancellationToken.None);

        Assert.Null(result.Response!.JobTitle);
    }

    [Fact]
    public async Task UpdateProfile_Should_ReturnNotFound_When_UserMissing()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();

        var result = await mediator.Send(new UserUpdateProfileCommand(Guid.NewGuid(), "A", null, null, null, null, null), CancellationToken.None);

        Assert.True(result.IsNotFound);
    }

    [Fact]
    public async Task ChangeUsername_Should_ReturnConflict_When_TakenByAnotherUser()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var first = await CreateUserAsync(mediator, "first", "first@example.com");
        await CreateUserAsync(mediator, "second", "second@example.com");

        var result = await mediator.Send(new UserChangeUsernameCommand(first.Id, "SECOND"), CancellationToken.None);

        Assert.NotNull(result.ConflictError);
        var unchanged = await mediator.Send(new UserGetQuery(first.Id), CancellationToken.None);
        Assert.Equal("first", unchanged!.Username);
    }

    [Fact]
    public async Task ChangeUsername_Should_Succeed_When_SameUsernameInDifferentCase()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserChangeUsernameCommand(created.Id, "ILYA"), CancellationToken.None);

        Assert.Null(result.ConflictError);
        Assert.Equal("ilya", result.Response!.Username);
    }

    [Fact]
    public async Task ChangeEmail_Should_ReturnConflict_When_TakenByAnotherUser()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var first = await CreateUserAsync(mediator, "first", "first@example.com");
        await CreateUserAsync(mediator, "second", "second@example.com");

        var result = await mediator.Send(new UserChangeEmailCommand(first.Id, "Second@Example.com"), CancellationToken.None);

        Assert.NotNull(result.ConflictError);
    }

    [Fact]
    public async Task SetLink_Should_ReplaceLinkOfSameType()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        await mediator.Send(new UserSetLinkCommand(created.Id, UserLinkType.GitHub, "https://github.com/old"), CancellationToken.None);
        var result = await mediator.Send(new UserSetLinkCommand(created.Id, UserLinkType.GitHub, "https://github.com/new"), CancellationToken.None);

        var link = Assert.Single(result.Response!.Links);
        Assert.Equal(UserLinkType.GitHub, link.Type);
        Assert.Equal("https://github.com/new", link.Url);
    }

    [Fact]
    public async Task RemoveLink_Should_Succeed_When_LinkMissing()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserRemoveLinkCommand(created.Id, UserLinkType.Telegram), CancellationToken.None);

        Assert.False(result.IsNotFound);
        Assert.Empty(result.Response!.Links);
    }

    [Fact]
    public async Task Deactivate_Should_HideUserFromDefaultList_And_Search()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserDeactivateCommand(created.Id), CancellationToken.None);

        Assert.False(result.Response!.IsActive);
        Assert.Empty(await mediator.Send(new UserListQuery(), CancellationToken.None));
        Assert.Single(await mediator.Send(new UserListQuery(IncludeInactive: true), CancellationToken.None));
        Assert.Empty(await mediator.Send(new UserSearchQuery("il"), CancellationToken.None));
    }

    [Fact]
    public async Task Deactivate_Twice_Should_Throw()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);
        await mediator.Send(new UserDeactivateCommand(created.Id), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Send(new UserDeactivateCommand(created.Id), CancellationToken.None));
    }

    [Fact]
    public async Task Activate_Should_RestoreUser()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);
        await mediator.Send(new UserDeactivateCommand(created.Id), CancellationToken.None);

        var result = await mediator.Send(new UserActivateCommand(created.Id), CancellationToken.None);

        Assert.True(result.Response!.IsActive);
        Assert.Single(await mediator.Send(new UserListQuery(), CancellationToken.None));
    }

    [Fact]
    public async Task GetByUsername_Should_IgnoreCase()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        var created = await CreateUserAsync(mediator);

        var found = await mediator.Send(new UserGetByUsernameQuery(" ILYA "), CancellationToken.None);

        Assert.Equal(created.Id, found!.Id);
    }

    [Fact]
    public async Task Search_Should_FindByUsernamePrefix_And_ByName()
    {
        var (mediator, _, _, _) = TestMediatorFactory.Create();
        await CreateUserAsync(mediator, "ilya", "ilya@example.com");
        await mediator.Send(new UserCreateCommand("aziz", "aziz@example.com", "Азиз", "Мамедов", "correct horse battery"), CancellationToken.None);

        var byUsername = await mediator.Send(new UserSearchQuery("il"), CancellationToken.None);
        var byName = await mediator.Send(new UserSearchQuery("Мамед"), CancellationToken.None);
        var empty = await mediator.Send(new UserSearchQuery("   "), CancellationToken.None);

        Assert.Equal("ilya", Assert.Single(byUsername).Username);
        Assert.Equal("aziz", Assert.Single(byName).Username);
        Assert.Empty(empty);
    }
}
