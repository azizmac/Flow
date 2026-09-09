using System.Net;
using System.Text.Json;

namespace Flow.Auth.Tests;

[Collection(AuthCollection.Name)]
public sealed class DiscoveryTests(AuthFixture auth)
{
    [Fact]
    public async Task Discovery_Should_Expose_Endpoints_And_Jwks()
    {
        using var client = auth.CreateClient();

        using var discovery = await client.GetAsync("/.well-known/openid-configuration");
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        using var json = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync());
        var root = json.RootElement;

        Assert.Equal("http://localhost/", root.GetProperty("issuer").GetString());
        Assert.EndsWith("/connect/authorize", root.GetProperty("authorization_endpoint").GetString());
        Assert.EndsWith("/connect/token", root.GetProperty("token_endpoint").GetString());
        Assert.EndsWith("/connect/endsession", root.GetProperty("end_session_endpoint").GetString());
        Assert.Contains("S256", root.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("flow-api", root.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()));

        using var jwks = await client.GetAsync(root.GetProperty("jwks_uri").GetString()!);
        Assert.Equal(HttpStatusCode.OK, jwks.StatusCode);
        using var keys = JsonDocument.Parse(await jwks.Content.ReadAsStringAsync());
        Assert.NotEmpty(keys.RootElement.GetProperty("keys").EnumerateArray());
    }
}
