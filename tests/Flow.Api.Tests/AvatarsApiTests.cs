using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// HTTP-поверхность аватара: multipart-загрузка своего аватара, коды отказа и маршрут картинки /avatars/…
/// вне префикса /api (тег img заголовков не носит). Правила формата и уборки старых файлов — в
/// Flow.Application.Tests (UserAvatarFeatureTests).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AvatarsApiTests(ApiFixture api)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7, 7];

    private static MultipartFormDataContent File(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    [Fact]
    public async Task Upload_Requires_Token()
    {
        using var anonymous = api.CreateClient();
        using var response = await anonymous.PutAsync("/api/users/me/avatar", File(Png, "a.png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Returns_Profile_And_Avatar_Is_Served_With_Safe_Headers()
    {
        using var client = api.CreateClientAs();

        using var uploaded = await client.PutAsync("/api/users/me/avatar", File(Png, "фото.png"));
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var user = (await uploaded.Content.ReadFromJsonAsync<UserResponse>())!;
        Assert.StartsWith($"/avatars/{user.Id}/", user.AvatarUrl);

        using var response = await client.GetAsync(user.AvatarUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.NotNull(response.Headers.CacheControl.MaxAge);
    }

    [Fact]
    public async Task Upload_Of_Non_Image_Returns_400()
    {
        using var client = api.CreateClientAs();

        using var response = await client.PutAsync("/api/users/me/avatar", File(Encoding.UTF8.GetBytes("<svg/>"), "a.svg"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Removed_Avatar_Is_No_Longer_Served()
    {
        using var client = api.CreateClientAs();
        using var uploaded = await client.PutAsync("/api/users/me/avatar", File(Png, "a.png"));
        var user = (await uploaded.Content.ReadFromJsonAsync<UserResponse>())!;

        using var removed = await client.DeleteAsync("/api/users/me/avatar");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Null((await removed.Content.ReadFromJsonAsync<UserResponse>())!.AvatarUrl);

        using var response = await client.GetAsync(user.AvatarUrl);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Avatar_Route_Should_Not_Fall_Through_To_Ui()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync("/avatars/не-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}
