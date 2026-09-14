using System.Net;
using Flow.Application.Features.Users.Commands.UserCreateCommand;
using Flow.Auth.Contracts;
using Flow.Auth.Data;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Auth.Tests;

/// <summary>
/// Публичный контракт Auth-модуля (IAccountService → AuthAccountManager): замена прежних тестов admin-API.
/// Проверяются хеш BCrypt, конфликты username/email, смена пароля с флагом MustChangePassword и блокировка.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class AccountManagerTests(AuthFixture auth)
{
    private const string Password = "correct horse battery";

    [Fact]
    public async Task UserCreate_Command_Should_Create_Profile_And_Login_Account()
    {
        await using var scope = auth.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new UserCreateCommand(
            AuthFixture.BootstrapId,
            "inprocess-user",
            "inprocess-user@example.com",
            "In",
            "Process",
            Password));

        Assert.NotNull(result.Response);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var account = await users.FindByIdAsync(result.Response.Id.ToString());
        Assert.NotNull(account);
        Assert.Equal(result.Response.Username, account.UserName);

        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "inprocess-user", Password, "/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(login.Headers.Location!));
    }

    [Fact]
    public async Task Create_Should_Store_BCrypt_Hash_And_Require_Change()
    {
        var id = Guid.NewGuid();
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        var result = await accounts.CreateAsync(id, "bcrypt-user", "bcrypt-user@example.com", Password, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(user);
        Assert.Equal("bcrypt-user", user!.UserName);
        Assert.StartsWith("$2a$12$", user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.False(await users.CheckPasswordAsync(user, "wrong"));
    }

    [Fact]
    public async Task Create_Duplicate_Username_Or_Email_Should_Return_Conflict()
    {
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateAsync(Guid.NewGuid(), "dup-user", "dup-user@example.com", Password, CancellationToken.None);

        var byName = await accounts.CreateAsync(Guid.NewGuid(), "DUP-USER", "other@example.com", Password, CancellationToken.None);
        var byEmail = await accounts.CreateAsync(Guid.NewGuid(), "other-name", "DUP-USER@example.com", Password, CancellationToken.None);

        Assert.Equal(AccountResultStatus.UsernameTaken, byName.Status);
        Assert.Equal(AccountResultStatus.EmailTaken, byEmail.Status);
    }

    [Fact]
    public async Task Create_ShortPassword_Should_Return_Invalid()
    {
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();

        var result = await accounts.CreateAsync(Guid.NewGuid(), "short-pw", "short-pw@example.com", "1234567", CancellationToken.None);

        Assert.Equal(AccountResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task ChangeUsername_And_Email_Should_Update_And_Reject_Duplicates()
    {
        var a = Guid.NewGuid();
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateAsync(a, "rename-a", "rename-a@example.com", Password, CancellationToken.None);
        await accounts.CreateAsync(Guid.NewGuid(), "rename-b", "rename-b@example.com", Password, CancellationToken.None);

        Assert.True((await accounts.ChangeUsernameAsync(a, "rename-c", CancellationToken.None)).IsSuccess);
        Assert.Equal(AccountResultStatus.UsernameTaken, (await accounts.ChangeUsernameAsync(a, "rename-b", CancellationToken.None)).Status);
        Assert.Equal(AccountResultStatus.EmailTaken, (await accounts.ChangeEmailAsync(a, "rename-b@example.com", CancellationToken.None)).Status);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal("rename-c", (await users.FindByIdAsync(a.ToString()))!.UserName);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_Should_Be_Invalid_Reset_Should_Set_MustChangePassword()
    {
        var id = Guid.NewGuid();
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateAsync(id, "pw-user", "pw-user@example.com", Password, CancellationToken.None);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var wrong = await accounts.ChangePasswordAsync(id, "nope", "another strong password", CancellationToken.None);
        Assert.Equal(AccountResultStatus.Invalid, wrong.Status);

        Assert.True((await accounts.ChangePasswordAsync(id, null, "another strong password", CancellationToken.None)).IsSuccess);
        Assert.True((await users.FindByIdAsync(id.ToString()))!.MustChangePassword);

        Assert.True((await accounts.ChangePasswordAsync(id, "another strong password", "self chosen 789", CancellationToken.None)).IsSuccess);
        Assert.False((await users.FindByIdAsync(id.ToString()))!.MustChangePassword);
        var changed = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(changed);
        Assert.True(await users.CheckPasswordAsync(changed, "self chosen 789"));
    }

    [Fact]
    public async Task Disable_Should_Lockout_And_Enable_Should_Restore()
    {
        var id = Guid.NewGuid();
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        await accounts.CreateAsync(id, "lock-user", "lock-user@example.com", Password, CancellationToken.None);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(user);

        await accounts.DisableAsync(id, CancellationToken.None);
        Assert.True(await users.IsLockedOutAsync(user));

        await accounts.EnableAsync(id, CancellationToken.None);
        Assert.False(await users.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Unknown_Account_Should_Return_Invalid_And_Disable_Should_Throw()
    {
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var missing = Guid.NewGuid();

        Assert.Equal(AccountResultStatus.Invalid, (await accounts.ChangeUsernameAsync(missing, "who", CancellationToken.None)).Status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DisableAsync(missing, CancellationToken.None));
    }
}
