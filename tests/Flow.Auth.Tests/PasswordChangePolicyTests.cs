using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Accounts;
using Microsoft.AspNetCore.WebUtilities;

namespace Flow.Auth.Tests;

/// <summary>Обязательная смена начального пароля (#26): bootstrap, созданные админом, сброшенные Owner'ом.</summary>
[Collection(AuthCollection.Name)]
public sealed class PasswordChangePolicyTests(AuthFixture auth)
{
    private const string Initial = "initial pass 123";
    private const string Chosen = "chosen by user 456";

    private static string AuthorizeUrl => "/connect/authorize?" + new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["client_id"] = "flow-client",
        ["redirect_uri"] = AuthFixture.ClientRedirectUri,
        ["response_type"] = "code",
        ["scope"] = "openid profile email offline_access flow-api",
        ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
        ["code_challenge_method"] = "S256"
    }).ReadAsStringAsync().Result;

    [Fact]
    public async Task Bootstrap_User_Login_Should_Redirect_To_ChangePassword()
    {
        using var client = auth.CreateClient();

        using var login = await LoginPage.PostAsync(client, "admin", "admin", "/connect/authorize?x=1");

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(login.Headers.Location!));
        Assert.Equal("/connect/authorize?x=1", QueryHelpers.ParseQuery(ChangePasswordPage.QueryOf(login.Headers.Location!))["ReturnUrl"].ToString());

        // Cookie есть, но authorize всё равно не выдаёт код, пока пароль не сменён.
        using var authorize = await client.GetAsync(AuthorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(authorize.Headers.Location!));
    }

    [Fact]
    public async Task Admin_Created_Account_Must_Change_Password_Then_Gets_Code()
    {
        var account = await auth.CreateAccountAsync("must-change", Initial, mustChangePassword: true);
        Assert.True(account.MustChangePassword);
        using var client = auth.CreateClient();

        using var login = await LoginPage.PostAsync(client, "must-change", Initial, "/");
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(login.Headers.Location!));

        using var mismatch = await ChangePasswordPage.PostAsync(client, Initial, Chosen, Chosen + "x", "/");
        Assert.Contains("не совпадают", await mismatch.Content.ReadAsStringAsync());

        using var tooShort = await ChangePasswordPage.PostAsync(client, Initial, "short", "short", "/");
        Assert.Contains("не короче 8", await tooShort.Content.ReadAsStringAsync());

        using var wrongCurrent = await ChangePasswordPage.PostAsync(client, "nope", Chosen, Chosen, "/");
        Assert.Contains("Неверный текущий", await wrongCurrent.Content.ReadAsStringAsync());

        using var changed = await ChangePasswordPage.PostAsync(client, Initial, Chosen, Chosen, "/connect/authorize?x=1");
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal("/connect/authorize?x=1", changed.Headers.Location!.ToString());

        using var authorize = await client.GetAsync(AuthorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, authorize.StatusCode);
        Assert.StartsWith(AuthFixture.ClientRedirectUri, authorize.Headers.Location!.ToString());
        Assert.True(QueryHelpers.ParseQuery(authorize.Headers.Location.Query).ContainsKey("code"));

        using var admin = await auth.CreateAdminClientAsync();
        var reloaded = await admin.GetFromJsonAsync<AccountResponse>($"/accounts/{account.Id}");
        Assert.False(reloaded!.MustChangePassword);

        // Новый пароль работает, старый — нет.
        using var fresh = auth.CreateClient();
        using var oldLogin = await LoginPage.PostAsync(fresh, "must-change", Initial);
        Assert.Contains("Неверный логин или пароль", await oldLogin.Content.ReadAsStringAsync());
        using var newLogin = await LoginPage.PostAsync(fresh, "must-change", Chosen, "/");
        Assert.Equal(HttpStatusCode.Redirect, newLogin.StatusCode);
        Assert.Equal("/", newLogin.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Owner_Reset_Should_Require_Change_And_Self_Change_Should_Clear_Flag()
    {
        var account = await auth.CreateAccountAsync("reset-flag");
        using var admin = await auth.CreateAdminClientAsync();

        using var reset = await admin.PostAsJsonAsync($"/accounts/{account.Id}/password", new ChangeAccountPasswordRequest(null, Initial));
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.True((await admin.GetFromJsonAsync<AccountResponse>($"/accounts/{account.Id}"))!.MustChangePassword);

        using var self = await admin.PostAsJsonAsync($"/accounts/{account.Id}/password", new ChangeAccountPasswordRequest(Initial, Chosen));
        Assert.Equal(HttpStatusCode.NoContent, self.StatusCode);
        Assert.False((await admin.GetFromJsonAsync<AccountResponse>($"/accounts/{account.Id}"))!.MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_Page_Without_Cookie_Should_Redirect_To_Login()
    {
        using var client = auth.CreateClient();

        using var response = await client.GetAsync(ChangePasswordPage.Path);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/login", response.Headers.Location!.AbsolutePath);
    }
}
