using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Flow.Auth.Tests;

/// <summary>Полный Authorization Code + PKCE как его пройдёт Flow.Client, плюс refresh и отказ после disable.</summary>
[Collection(AuthCollection.Name)]
public sealed class CodeFlowTests(AuthFixture auth)
{
    private const string Scope = "openid profile email offline_access flow-api";

    [Fact]
    public async Task CodeFlow_Should_Issue_Jwt_For_FlowApi_And_Refresh()
    {
        var account = await auth.CreateAccountAsync("code-user");
        using var client = auth.CreateClient();

        var tokens = await AuthorizeAsync(client, "code-user", "correct horse battery");

        var access = Jwt.Payload(tokens.GetProperty("access_token").GetString()!);
        Assert.Equal(account.Id.ToString(), access.GetProperty("sub").GetString());
        Assert.Equal("flow-api", access.GetProperty("aud").GetString());
        Assert.Equal("http://localhost/", access.GetProperty("iss").GetString());
        Assert.Equal("code-user", access.GetProperty("preferred_username").GetString());
        Assert.Equal("code-user@example.com", access.GetProperty("email").GetString());
        Assert.False(access.TryGetProperty("role", out _));

        var idToken = Jwt.Payload(tokens.GetProperty("id_token").GetString()!);
        Assert.Equal("code-user", idToken.GetProperty("name").GetString());
        Assert.Equal("flow-client", idToken.GetProperty("aud").GetString());

        var refreshed = await RefreshAsync(client, tokens.GetProperty("refresh_token").GetString()!);
        Assert.Equal(HttpStatusCode.OK, refreshed.Status);
        Assert.Equal(account.Id.ToString(), Jwt.Payload(refreshed.Body.GetProperty("access_token").GetString()!).GetProperty("sub").GetString());
    }

    [Fact]
    public async Task Refresh_After_Disable_Should_Return_InvalidGrant()
    {
        var account = await auth.CreateAccountAsync("refresh-disabled");
        using var client = auth.CreateClient();
        var tokens = await AuthorizeAsync(client, "refresh-disabled", "correct horse battery");

        using var admin = await auth.CreateAdminClientAsync();
        using var disable = await admin.PostAsync($"/accounts/{account.Id}/disable", null);
        Assert.Equal(HttpStatusCode.NoContent, disable.StatusCode);

        var refreshed = await RefreshAsync(client, tokens.GetProperty("refresh_token").GetString()!);

        Assert.Equal(HttpStatusCode.BadRequest, refreshed.Status);
        Assert.Equal("invalid_grant", refreshed.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Authorize_Without_Pkce_Should_Be_Rejected()
    {
        using var client = auth.CreateClient();
        var url = "/connect/authorize?" + new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "flow-client",
            ["redirect_uri"] = AuthFixture.ClientRedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope
        }).ReadAsStringAsync().Result;

        using var response = await client.GetAsync(url);

        // OpenIddict отвечает ошибкой invalid_request (страница ошибки или редирект с error=) — но не отправляет на вход.
        Assert.NotEqual("/account/login", response.Headers.Location?.AbsolutePath);
        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Redirect);
        if (response.StatusCode == HttpStatusCode.Redirect)
            Assert.Contains("error=invalid_request", response.Headers.Location!.ToString());
    }

    /// <summary>authorize → login → authorize → redirect_uri?code= → token. Возвращает JSON ответа token endpoint.</summary>
    private static async Task<JsonElement> AuthorizeAsync(HttpClient client, string login, string password)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var authorizeUrl = "/connect/authorize?" + await new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "flow-client",
            ["redirect_uri"] = AuthFixture.ClientRedirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = "s1",
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256"
        }).ReadAsStringAsync();

        // 1. Без cookie — на страницу входа.
        using var first = await client.GetAsync(authorizeUrl);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/account/login", first.Headers.Location!.AbsolutePath);
        var returnUrl = QueryHelpers.ParseQuery(first.Headers.Location.Query)["ReturnUrl"].ToString();

        // 2. Вход → обратно на authorize.
        using var login2 = await LoginPage.PostAsync(client, login, password, returnUrl);
        Assert.Equal(HttpStatusCode.Redirect, login2.StatusCode);

        // 3. С cookie — редирект на redirect_uri клиента с кодом.
        using var second = await client.GetAsync(login2.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        var location = second.Headers.Location!;
        Assert.StartsWith(AuthFixture.ClientRedirectUri, location.ToString());
        var query = QueryHelpers.ParseQuery(location.Query);
        Assert.Equal("s1", query["state"].ToString());
        var code = query["code"].ToString();
        Assert.False(string.IsNullOrEmpty(code));

        // 4. Обмен кода на токены.
        using var token = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "flow-client",
            ["code"] = code,
            ["redirect_uri"] = AuthFixture.ClientRedirectUri,
            ["code_verifier"] = verifier
        }));
        var body = await token.Content.ReadAsStringAsync();
        Assert.True(token.StatusCode == HttpStatusCode.OK, body);

        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = "flow-client",
            ["refresh_token"] = refreshToken
        }));

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        return (response.StatusCode, body);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
