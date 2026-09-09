using System.Net;
using Flow.Auth.Data;
using Flow.Auth.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Auth.Tests;

[Collection(AuthCollection.Name)]
public sealed class LoginTests(AuthFixture auth)
{
    [Fact]
    public async Task Login_ByUsername_And_ByEmail_Should_Redirect_To_ReturnUrl()
    {
        await auth.CreateAccountAsync("login-user");

        using var byName = auth.CreateClient();
        using var r1 = await LoginPage.PostAsync(byName, "login-user", "correct horse battery", "/connect/authorize?x=1");
        Assert.Equal(HttpStatusCode.Redirect, r1.StatusCode);
        Assert.Equal("/connect/authorize?x=1", r1.Headers.Location!.ToString());

        using var byEmail = auth.CreateClient();
        using var r2 = await LoginPage.PostAsync(byEmail, "LOGIN-USER@example.com", "correct horse battery", "/");
        Assert.Equal(HttpStatusCode.Redirect, r2.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Should_Show_Same_Error_As_Unknown_Login()
    {
        await auth.CreateAccountAsync("wrong-pw");
        using var client = auth.CreateClient();

        using var wrong = await LoginPage.PostAsync(client, "wrong-pw", "not it");
        using var unknown = await LoginPage.PostAsync(client, "nobody-here", "not it");

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Неверный логин или пароль", await wrong.Content.ReadAsStringAsync());
        Assert.Contains("Неверный логин или пароль", await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_After5Failures_Should_LockOut_Even_With_CorrectPassword()
    {
        await auth.CreateAccountAsync("lockout-user");
        using var client = auth.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            using var failed = await LoginPage.PostAsync(client, "lockout-user", "wrong");
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        using var locked = await LoginPage.PostAsync(client, "lockout-user", "correct horse battery", "/");

        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);
        Assert.Contains("Слишком много попыток", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Disabled_Account_Should_Be_Refused_On_Login()
    {
        var account = await auth.CreateAccountAsync("disabled-user");
        using var admin = await auth.CreateAdminClientAsync();
        using var disable = await admin.PostAsync($"/accounts/{account.Id}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "disabled-user", "correct horse battery", "/");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("Доступ закрыт", await login.Content.ReadAsStringAsync());

        using var enable = await admin.PostAsync($"/accounts/{account.Id}/enable", null);
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);
        using var again = await LoginPage.PostAsync(client, "disabled-user", "correct horse battery", "/");
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_User_Should_Exist_And_Login_With_Short_Password()
    {
        await using var scope = auth.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(AuthFixture.BootstrapId.ToString());
        Assert.NotNull(user);
        Assert.Equal("admin", user!.UserName);
        Assert.Equal("admin@flow.com", user.Email);
        Assert.StartsWith("$2a$12$", user.PasswordHash);

        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "admin@flow.com", "admin", "/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        // Пароль bootstrap-овский — сначала на смену (#26).
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(login.Headers.Location!));
        Assert.True(user.MustChangePassword);
    }

    [Fact]
    public async Task Bootstrap_Seeder_Should_Be_Idempotent()
    {
        await using var scope = auth.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<BootstrapUserSeeder>();

        var created = await seeder.SeedAsync(CancellationToken.None);

        Assert.False(created);
    }
}
