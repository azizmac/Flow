using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Bootstrap;
using Flow.Application.Features.Users.Commands.UserActivateCommand;
using Flow.Application.Features.Users.Commands.UserChangeEmailCommand;
using Flow.Application.Features.Users.Commands.UserChangePasswordCommand;
using Flow.Application.Features.Users.Commands.UserChangeUsernameCommand;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Application.Features.Users.Commands.UserDeactivateCommand;
using Flow.Application.Features.Users.Queries.UserGetMeQuery;
using Flow.Application.Tests.Fakes;
using MediatR;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>Связка команд Users с Flow.Auth (IAccountService) и bootstrap-профиль. См. docs/TZ_auth.md, #23.</summary>
public class AccountFeatureTests
{
    private const string Password = "correct horse battery";

    private static async Task<Guid> CreateUserAsync(IMediator mediator, string username = "ilya")
    {
        var result = await mediator.Send(new UserCreateCommand(username, $"{username}@example.com", "Илья", "Моторин", Password), CancellationToken.None);
        Assert.False(result.IsConflict);
        return result.Response!.Id;
    }

    // ---- bootstrap ----

    [Fact]
    public async Task SeedBootstrapUser_Should_CreateProfile_WithGivenId_And_NotCallAuth()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        var created = await mediator.Send(new SeedBootstrapUserCommand(id, "Admin", "Admin@Flow.com", "Admin", "Flow"), CancellationToken.None);

        Assert.True(created);
        var user = await users.GetByIdAsync(id, CancellationToken.None);
        Assert.NotNull(user);
        Assert.Equal("admin", user!.Username);
        Assert.Equal("admin@flow.com", user.Email);
        Assert.True(user.IsActive);
        Assert.Empty(accounts.Calls);
    }

    [Fact]
    public async Task SeedBootstrapUser_Twice_Should_BeNoop()
    {
        var (mediator, _, _, users, _) = TestMediatorFactory.CreateWithAccounts();
        var command = new SeedBootstrapUserCommand(Guid.NewGuid(), "admin", "admin@flow.com", "Admin", "Flow");
        await mediator.Send(command, CancellationToken.None);

        var created = await mediator.Send(command with { FirstName = "Changed" }, CancellationToken.None);

        Assert.False(created);
        Assert.Equal("Admin", (await users.GetByIdAsync(command.Id, CancellationToken.None))!.FirstName);
    }

    // ---- create ----

    [Fact]
    public async Task Create_Should_CreateAccount_WithSameId_Normalized_Values_And_Password()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();

        var result = await mediator.Send(new UserCreateCommand(" Ilya ", " Ilya@Example.COM ", "Илья", "Моторин", Password), CancellationToken.None);

        var call = Assert.Single(accounts.CallsTo("Create"));
        Assert.Equal(result.Response!.Id, call.Id);
        Assert.Equal("ilya", call.Username);
        Assert.Equal("ilya@example.com", call.Email);
        Assert.Equal(Password, call.Password);
    }

    [Fact]
    public async Task Create_When_Auth_Says_UsernameTaken_Should_Return409_And_NotCreateProfile()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        accounts.NextResult = AccountResult.UsernameTaken("Username 'ilya' is already taken.");

        var result = await mediator.Send(new UserCreateCommand("ilya", "ilya@example.com", "Илья", "Моторин", Password), CancellationToken.None);

        Assert.True(result.IsUsernameTaken);
        Assert.Null(await users.GetByUsernameAsync("ilya", CancellationToken.None));
    }

    [Fact]
    public async Task Create_When_Auth_Says_EmailTaken_Should_Return409()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        accounts.NextResult = AccountResult.EmailTaken("Email 'ilya@example.com' is already taken.");

        var result = await mediator.Send(new UserCreateCommand("ilya", "ilya@example.com", "Илья", "Моторин", Password), CancellationToken.None);

        Assert.True(result.IsEmailTaken);
    }

    [Fact]
    public async Task Create_When_Auth_Rejects_Password_Should_Throw_ArgumentException_And_NotCreateProfile()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        accounts.NextResult = AccountResult.Invalid("Passwords must be at least 8 characters.");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new UserCreateCommand("ilya", "ilya@example.com", "Илья", "Моторин", "short"), CancellationToken.None));

        Assert.Contains("at least 8", ex.Message);
        Assert.Null(await users.GetByUsernameAsync("ilya", CancellationToken.None));
    }

    [Fact]
    public async Task Create_When_LocalCopy_Has_Username_Should_NotCallAuth()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        await CreateUserAsync(mediator);
        accounts.Calls.Clear();

        var result = await mediator.Send(new UserCreateCommand("ILYA", "other@example.com", "A", "B", Password), CancellationToken.None);

        Assert.True(result.IsUsernameTaken);
        Assert.Empty(accounts.Calls);
    }

    // ---- username / email ----

    [Fact]
    public async Task ChangeUsername_Should_UpdateAuth_Then_LocalCopy()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserChangeUsernameCommand(id, "Ilya.M"), CancellationToken.None);

        Assert.Equal("ilya.m", result.Response!.Username);
        var call = Assert.Single(accounts.CallsTo("ChangeUsername"));
        Assert.Equal("ilya.m", call.Username);
        Assert.Equal("ilya.m", (await users.GetByIdAsync(id, CancellationToken.None))!.Username);
    }

    [Fact]
    public async Task ChangeUsername_When_Auth_Conflicts_Should_Return409_And_KeepLocalCopy()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        accounts.NextResult = AccountResult.UsernameTaken("Username 'taken' is already taken.");

        var result = await mediator.Send(new UserChangeUsernameCommand(id, "taken"), CancellationToken.None);

        Assert.NotNull(result.ConflictError);
        Assert.Equal("ilya", (await users.GetByIdAsync(id, CancellationToken.None))!.Username);
    }

    [Fact]
    public async Task ChangeUsername_SameValue_Should_NotCallAuth()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        accounts.Calls.Clear();

        var result = await mediator.Send(new UserChangeUsernameCommand(id, "ILYA"), CancellationToken.None);

        Assert.Null(result.ConflictError);
        Assert.Empty(accounts.Calls);
    }

    [Fact]
    public async Task ChangeEmail_When_Auth_Conflicts_Should_Return409_And_KeepLocalCopy()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        accounts.NextResult = AccountResult.EmailTaken("Email 'taken@example.com' is already taken.");

        var result = await mediator.Send(new UserChangeEmailCommand(id, "taken@example.com"), CancellationToken.None);

        Assert.NotNull(result.ConflictError);
        Assert.Equal("ilya@example.com", (await users.GetByIdAsync(id, CancellationToken.None))!.Email);
    }

    // ---- password ----

    [Fact]
    public async Task ChangePassword_Should_PassThrough_To_Auth()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserChangePasswordCommand(id, Password, "new strong password"), CancellationToken.None);

        Assert.NotNull(result.Response);
        var call = Assert.Single(accounts.CallsTo("ChangePassword"));
        Assert.Equal(Password, call.CurrentPassword);
        Assert.Equal("new strong password", call.Password);
    }

    [Fact]
    public async Task ChangePassword_When_Auth_Rejects_Should_Throw_ArgumentException()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        accounts.NextResult = AccountResult.Invalid("Incorrect password.");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            mediator.Send(new UserChangePasswordCommand(id, "wrong", "new strong password"), CancellationToken.None));
    }

    [Fact]
    public async Task ChangePassword_UnknownUser_Should_ReturnNotFound_And_NotCallAuth()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();

        var result = await mediator.Send(new UserChangePasswordCommand(Guid.NewGuid(), null, "new strong password"), CancellationToken.None);

        Assert.True(result.IsNotFound);
        Assert.Empty(accounts.Calls);
    }

    // ---- deactivate / activate ----

    [Fact]
    public async Task Deactivate_Should_DisableAccount_Then_ChangeStatus()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);

        var result = await mediator.Send(new UserDeactivateCommand(id), CancellationToken.None);

        Assert.False(result.Response!.IsActive);
        Assert.Equal(id, Assert.Single(accounts.CallsTo("Disable")).Id);
        Assert.False((await users.GetByIdAsync(id, CancellationToken.None))!.IsActive);
    }

    [Fact]
    public async Task Deactivate_When_Auth_Unavailable_Should_Throw_And_KeepStatus()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        accounts.DisableEnableException = new AuthUnavailableException("down");

        await Assert.ThrowsAsync<AuthUnavailableException>(() => mediator.Send(new UserDeactivateCommand(id), CancellationToken.None));

        Assert.True((await users.GetByIdAsync(id, CancellationToken.None))!.IsActive);
    }

    [Fact]
    public async Task Activate_Should_EnableAccount_Then_ChangeStatus()
    {
        var (mediator, _, _, users, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        await mediator.Send(new UserDeactivateCommand(id), CancellationToken.None);

        var result = await mediator.Send(new UserActivateCommand(id), CancellationToken.None);

        Assert.True(result.Response!.IsActive);
        Assert.Single(accounts.CallsTo("Enable"));
        Assert.True((await users.GetByIdAsync(id, CancellationToken.None))!.IsActive);
    }

    [Fact]
    public async Task Deactivate_AlreadyDeactivated_Should_Throw_And_NotCallAuthAgain()
    {
        var (mediator, _, _, _, accounts) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);
        await mediator.Send(new UserDeactivateCommand(id), CancellationToken.None);
        accounts.Calls.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => mediator.Send(new UserDeactivateCommand(id), CancellationToken.None));

        Assert.Empty(accounts.Calls);
    }

    // ---- me ----

    [Fact]
    public async Task GetMe_Should_ReturnProfile_Or_Null()
    {
        var (mediator, _, _, _, _) = TestMediatorFactory.CreateWithAccounts();
        var id = await CreateUserAsync(mediator);

        var me = await mediator.Send(new UserGetMeQuery(id), CancellationToken.None);
        var nobody = await mediator.Send(new UserGetMeQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(id, me!.Id);
        Assert.Null(nobody);
    }
}
