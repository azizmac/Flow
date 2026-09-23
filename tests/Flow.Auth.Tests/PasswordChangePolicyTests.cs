using System.Net;
using Flow.Auth.Contracts;
using Flow.Auth.Data;
using Flow.Auth.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

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

        await using var scope = auth.CreateScope();
        var reloaded = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(account.Id.ToString());
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
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        // Сброс без текущего пароля (Owner) — ставит MustChangePassword.
        Assert.True((await accounts.ChangePasswordAsync(account.Id, null, Initial, CancellationToken.None)).IsSuccess);
        Assert.True((await users.FindByIdAsync(account.Id.ToString()))!.MustChangePassword);

        // Смена со своим текущим — снимает флаг.
        Assert.True((await accounts.ChangePasswordAsync(account.Id, Initial, Chosen, CancellationToken.None)).IsSuccess);
        Assert.False((await users.FindByIdAsync(account.Id.ToString()))!.MustChangePassword);
    }

    [Fact]
    public async Task Keep_Should_Warn_First_Then_Accept_Risk_And_Continue_Login()
    {
        var account = await auth.CreateAccountAsync("keep-as-is", Initial, mustChangePassword: true);
        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "keep-as-is", Initial, "/");
        Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(login.Headers.Location!));

        using var page = await client.GetAsync(ChangePasswordPage.Path);
        Assert.Contains("Оставить пароль как есть", await page.Content.ReadAsStringAsync());

        // Первый клик — только предупреждение: флаг на месте, код по-прежнему не выдаётся.
        using var warning = await ChangePasswordPage.KeepAsync(client, confirm: false, "/connect/authorize?x=1");
        Assert.Equal(HttpStatusCode.OK, warning.StatusCode);
        Assert.Contains("риск на себя", await warning.Content.ReadAsStringAsync());
        await using (var scope = auth.CreateScope())
        {
            var pending = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(account.Id.ToString());
            Assert.True(pending!.MustChangePassword);
            Assert.Null(pending.PasswordRiskAcceptedAt);
        }
        using (var blocked = await client.GetAsync(AuthorizeUrl))
            Assert.Equal(ChangePasswordPage.Path, ChangePasswordPage.PathOf(blocked.Headers.Location!));

        // Подтверждение — вход продолжается туда же, куда вела смена пароля, согласие записано.
        using var accepted = await ChangePasswordPage.KeepAsync(client, confirm: true, "/connect/authorize?x=1");
        Assert.Equal(HttpStatusCode.Redirect, accepted.StatusCode);
        Assert.Equal("/connect/authorize?x=1", accepted.Headers.Location!.ToString());

        using var authorize = await client.GetAsync(AuthorizeUrl);
        Assert.StartsWith(AuthFixture.ClientRedirectUri, authorize.Headers.Location!.ToString());
        Assert.True(QueryHelpers.ParseQuery(authorize.Headers.Location.Query).ContainsKey("code"));

        await using var after = auth.CreateScope();
        var reloaded = await after.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByIdAsync(account.Id.ToString());
        Assert.False(reloaded!.MustChangePassword);
        Assert.NotNull(reloaded.PasswordRiskAcceptedAt);

        // Прежний пароль остался рабочим — именно это человек и выбрал.
        using var fresh = auth.CreateClient();
        using var again = await LoginPage.PostAsync(fresh, "keep-as-is", Initial, "/");
        Assert.Equal("/", again.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Password_Reset_Should_Forget_Accepted_Risk()
    {
        var account = await auth.CreateAccountAsync("keep-then-reset");
        await using var scope = auth.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IAccountService>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = (await users.FindByIdAsync(account.Id.ToString()))!;
        user.PasswordRiskAcceptedAt = DateTimeOffset.UtcNow;
        await users.UpdateAsync(user);

        Assert.True((await accounts.ChangePasswordAsync(account.Id, null, Initial, CancellationToken.None)).IsSuccess);

        var reset = (await users.FindByIdAsync(account.Id.ToString()))!;
        Assert.True(reset.MustChangePassword);
        Assert.Null(reset.PasswordRiskAcceptedAt);
    }

    [Fact]
    public async Task Bootstrap_Seeder_Should_Not_Reflag_Default_Password_After_Accepted_Risk()
    {
        await using var scope = auth.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var seeder = scope.ServiceProvider.GetRequiredService<BootstrapUserSeeder>();
        var admin = (await users.FindByNameAsync("admin"))!;

        try
        {
            admin.MustChangePassword = false;
            admin.PasswordRiskAcceptedAt = DateTimeOffset.UtcNow;
            await users.UpdateAsync(admin);

            await seeder.SeedAsync(CancellationToken.None);
            Assert.False((await users.FindByNameAsync("admin"))!.MustChangePassword);

            // Без согласия стандартный пароль снова требует смены — прежнее правило не ослабло.
            admin.PasswordRiskAcceptedAt = null;
            await users.UpdateAsync(admin);
            await seeder.SeedAsync(CancellationToken.None);
            Assert.True((await users.FindByNameAsync("admin"))!.MustChangePassword);
        }
        finally
        {
            // Общая фикстура: вернуть базового пользователя в исходное состояние для остальных тестов.
            admin.MustChangePassword = true;
            admin.PasswordRiskAcceptedAt = null;
            await users.UpdateAsync(admin);
        }
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
