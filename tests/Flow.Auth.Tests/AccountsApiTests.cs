using System.Net;
using System.Net.Http.Json;
using Flow.Auth.Data;
using Flow.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Flow.Auth.Tests;

[Collection(AuthCollection.Name)]
public sealed class AccountsApiTests(AuthFixture auth)
{
    [Fact]
    public async Task Accounts_WithoutToken_Should_Return401()
    {
        using var client = auth.CreateClient();

        using var response = await client.PostAsJsonAsync("/accounts", new CreateAccountRequest(Guid.NewGuid(), "anon", "anon@example.com", "correct horse battery"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Accounts_WithToken_Without_AdminScope_Should_Return401()
    {
        using var client = auth.CreateClient();
        // Токен flow-api без auth:admin не получает audience flow-auth — валидация admin-API его не принимает вовсе.
        var token = await AuthFixture.GetClientCredentialsTokenAsync(client, "flow-api", AuthFixture.ApiClientSecret, "");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        using var response = await client.GetAsync($"/accounts/{AuthFixture.BootstrapId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_Should_Return201_And_Hash_With_BCrypt()
    {
        var id = Guid.NewGuid();

        var account = await auth.CreateAccountAsync("bcrypt-user", id: id);

        Assert.Equal(id, account.Id);
        Assert.Equal("bcrypt-user", account.Username);
        Assert.False(account.IsDisabled);

        await using var scope = auth.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(id.ToString());
        Assert.NotNull(user);
        Assert.StartsWith("$2a$12$", user!.PasswordHash);
        Assert.True(await users.CheckPasswordAsync(user, "correct horse battery"));
        Assert.False(await users.CheckPasswordAsync(user, "wrong"));
    }

    [Fact]
    public async Task Create_DuplicateUsername_Should_Return409()
    {
        await auth.CreateAccountAsync("dup-user");
        using var admin = await auth.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/accounts", new CreateAccountRequest(Guid.NewGuid(), "DUP-USER", "other@example.com", "correct horse battery"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ShortPassword_Should_Return400()
    {
        using var admin = await auth.CreateAdminClientAsync();

        using var response = await admin.PostAsJsonAsync("/accounts", new CreateAccountRequest(Guid.NewGuid(), "short-pw", "short-pw@example.com", "1234567"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ChangeUsername_And_Email_Should_Update_And_Reject_Duplicates()
    {
        var a = await auth.CreateAccountAsync("rename-a");
        await auth.CreateAccountAsync("rename-b");
        using var admin = await auth.CreateAdminClientAsync();

        using var ok = await admin.PatchAsJsonAsync($"/accounts/{a.Id}/username", new ChangeAccountUsernameRequest("rename-c"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        using var conflict = await admin.PatchAsJsonAsync($"/accounts/{a.Id}/username", new ChangeAccountUsernameRequest("rename-b"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var emailConflict = await admin.PatchAsJsonAsync($"/accounts/{a.Id}/email", new ChangeAccountEmailRequest("rename-b@example.com"));
        Assert.Equal(HttpStatusCode.Conflict, emailConflict.StatusCode);

        var loaded = await admin.GetFromJsonAsync<AccountResponse>($"/accounts/{a.Id}");
        Assert.Equal("rename-c", loaded!.Username);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrent_Should_Return400_And_Reset_Should_Work()
    {
        var account = await auth.CreateAccountAsync("pw-user");
        using var admin = await auth.CreateAdminClientAsync();

        using var wrong = await admin.PostAsJsonAsync($"/accounts/{account.Id}/password", new ChangeAccountPasswordRequest("nope", "another strong password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        using var reset = await admin.PostAsJsonAsync($"/accounts/{account.Id}/password", new ChangeAccountPasswordRequest(null, "another strong password"));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "pw-user", "another strong password", "/");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    }

    [Fact]
    public async Task Unknown_Account_Should_Return404()
    {
        using var admin = await auth.CreateAdminClientAsync();

        using var response = await admin.PostAsync($"/accounts/{Guid.NewGuid()}/disable", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
