using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.About;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Экран «Настройки»: только то, что видно по HTTP — маршрут me/preferences не спутан с {id:guid},
/// PATCH разбирает тело, недопустимое значение даёт 400, /about открыт любой роли. Правила — в Application.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SettingsApiTests(ApiFixture api)
{
    [Fact]
    public async Task Preferences_Should_RoundTrip_Through_Patch()
    {
        using var client = api.CreateClientAs();

        using var patch = await client.PatchAsJsonAsync("/api/users/me/preferences", new UpdateUserPreferencesRequest(TasksPageSize: 50));
        Assert.Equal(HttpStatusCode.OK, patch.StatusCode);

        var preferences = await client.GetFromJsonAsync<UserPreferencesResponse>("/api/users/me/preferences");
        Assert.Equal(50, preferences!.TasksPageSize);
        Assert.Contains(100, preferences.AllowedTasksPageSizes);

        // Вернуть как было: фикстура общая на коллекцию.
        await client.PatchAsJsonAsync("/api/users/me/preferences", new UpdateUserPreferencesRequest(TasksPageSize: 100));
    }

    [Fact]
    public async Task Preferences_With_Invalid_PageSize_Should_Return400()
    {
        using var client = api.CreateClientAs();

        using var response = await client.PatchAsJsonAsync("/api/users/me/preferences", new UpdateUserPreferencesRequest(TasksPageSize: 30));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task About_Should_Answer_Any_Role()
    {
        using var client = api.CreateClientAs();

        var about = await client.GetFromJsonAsync<AboutResponse>("/api/about");

        Assert.NotNull(about);
        Assert.False(string.IsNullOrWhiteSpace(about!.Version));
        Assert.True(about.Limits.MaxFileBytes > 0);
    }
}
