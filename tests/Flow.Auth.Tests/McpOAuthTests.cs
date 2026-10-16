using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Flow.Application.Features.Bootstrap;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Flow.Auth.Tests;

/// <summary>
/// Путь клиента MCP (Claude Code, IDE) к токену так, как его описывает спецификация MCP: 401 с resource_metadata →
/// метаданные защищённого ресурса (RFC 9728) → метаданные сервера авторизации (RFC 8414) → code + PKCE клиентом
/// flow-mcp с resource (RFC 8707) → вызов /mcp. Здесь, а не в Flow.Api.Tests: там схема подменена на JwtBearer,
/// и ни диспетчер схем, ни настоящая OpenIddict Validation не участвуют.
/// </summary>
[Collection(AuthCollection.Name)]
public sealed class McpOAuthTests(AuthFixture auth)
{
    private const string McpResource = "http://localhost/mcp";
    private const string McpRedirectUri = "http://localhost:33418/callback";

    [Fact]
    public async Task Unauthorized_Mcp_Should_Point_To_Resource_Metadata()
    {
        using var client = auth.CreateApiClient();

        using var response = await client.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/mcp\"",
            response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Metadata_Should_Lead_To_Flow_Authorization_Server()
    {
        using var client = auth.CreateApiClient();

        var resource = await client.GetFromJsonAsync<JsonElement>("/.well-known/oauth-protected-resource/mcp");
        Assert.Equal(McpResource, resource.GetProperty("resource").GetString());
        var server = resource.GetProperty("authorization_servers")[0].GetString()!;
        Assert.Contains("flow-api", resource.GetProperty("scopes_supported").EnumerateArray().Select(s => s.GetString()));

        // Без второго адреса discovery клиент MCP получил бы 302 на страницу входа вместо 404 и не дошёл бы до OIDC.
        var metadata = await client.GetFromJsonAsync<JsonElement>(new Uri(new Uri(server), "/.well-known/oauth-authorization-server"));
        Assert.EndsWith("/connect/authorize", metadata.GetProperty("authorization_endpoint").GetString());
        Assert.EndsWith("/connect/token", metadata.GetProperty("token_endpoint").GetString());
    }

    /// <summary>
    /// Адреса возврата клиентов MCP (docs/MCP.md) из appsettings.json. OpenIddict сверяет redirect_uri строкой целиком,
    /// поэтому каждый адрес проверяется в том виде, в каком его шлёт клиент: опечатка или лишний «/» в настройке
    /// дали бы invalid_request только у живого пользователя этого клиента.
    /// </summary>
    [Theory]
    [InlineData("http://localhost:33418/callback")]          // Claude Code (--callback-port 33418), Gemini CLI, Codex
    [InlineData("http://127.0.0.1:33418/callback")]
    [InlineData("http://127.0.0.1:33418")]                   // VS Code
    [InlineData("https://vscode.dev/redirect")]              // VS Code
    [InlineData("http://127.0.0.1:19876/mcp/oauth/callback")] // OpenCode
    [InlineData("cursor://anysphere.cursor-mcp/oauth/callback")] // Cursor
    [InlineData("https://claude.ai/api/mcp/auth_callback")]  // Claude (веб, Desktop) — коннектор
    public async Task Known_Mcp_Client_Redirects_Should_Get_A_Code(string redirectUri)
    {
        using var client = auth.CreateClient();
        using var login = await LoginPage.PostAsync(client, "admin", "admin");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var response = await client.GetAsync("/connect/authorize?" + await new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = "flow-mcp",
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = "flow-api offline_access",
            ["resource"] = McpResource,
            ["code_challenge"] = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            ["code_challenge_method"] = "S256"
        }).ReadAsStringAsync());

        // Bootstrap-админ обязан сменить пароль: OpenIddict уже принял запрос, и вход уводит на смену пароля,
        // а не на страницу ошибки. Неизвестный redirect_uri OpenIddict отклонил бы раньше — 400 без редиректа.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.True(location.StartsWith(redirectUri, StringComparison.Ordinal) || location.StartsWith("/account/change-password", StringComparison.Ordinal), location);
        Assert.DoesNotContain("error=", location);
    }

    [Fact]
    public async Task Code_Flow_With_Mcp_Client_And_Resource_Should_Open_Mcp()
    {
        var account = await auth.CreateAccountAsync("mcp-user");
        await using (var scope = auth.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMediator>().Send(new SeedBootstrapUserCommand(
                account.Id, account.UserName!, account.Email!, "Mcp", "User"));
        }

        using var login = auth.CreateClient();
        var tokens = await CodeFlowTests.AuthorizeAsync(login, "mcp-user", "correct horse battery",
            scope: "flow-api offline_access", clientId: "flow-mcp", redirectUri: McpRedirectUri, resource: McpResource);
        Assert.True(tokens.TryGetProperty("refresh_token", out _));
        var audiences = Jwt.Payload(tokens.GetProperty("access_token").GetString()!).GetProperty("aud");
        Assert.Contains("flow-api", audiences.EnumerateArray().Select(a => a.GetString()));
        Assert.Contains(McpResource, audiences.EnumerateArray().Select(a => a.GetString()));

        using var http = auth.CreateApiClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", tokens.GetProperty("access_token").GetString());
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http, ownsHttpClient: false));

        var result = await mcp.CallToolAsync("whoami");

        Assert.NotEqual(true, result.IsError);
        Assert.Contains("mcp-user", string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text)));
    }
}
