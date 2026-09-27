using System.Net;
using System.Text.Json;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Swagger (OpenApi/SwaggerSetup): документ и UI закрыты так же, как всё остальное, а в документе — ровно JSON-API
/// под /api. UI отдаёт middleware, а не эндпоинт, поэтому закрытым его держит только порядок в конвейере
/// (после UseAuthorization) — переставь вызов выше, и страница станет анонимной без единой ошибки сборки.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SwaggerTests(ApiFixture api)
{
    [Theory]
    [InlineData("/swagger/v1/swagger.json")]
    [InlineData("/swagger/index.html")]
    public async Task Swagger_Without_Session_Should_Be_Closed(string url)
    {
        using var client = api.CreateClient(allowAutoRedirect: false);

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Swagger_Ui_Should_Open_For_Signed_In_User()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync("/swagger/index.html");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Document_Should_Describe_Only_Api_With_Oauth2()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync("/swagger/v1/swagger.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var paths = root.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("/api/boards", paths);
        // Выход страниц и /files/{id} — не JSON-API: первый исключён явно, у второго нет [ApiController].
        Assert.All(paths, path => Assert.StartsWith("/api/", path));

        var flow = root.GetProperty("components").GetProperty("securitySchemes")
            .GetProperty("oauth2").GetProperty("flows").GetProperty("authorizationCode");
        Assert.Equal("/connect/authorize", flow.GetProperty("authorizationUrl").GetString());
        Assert.Equal("/connect/token", flow.GetProperty("tokenUrl").GetString());
    }
}
