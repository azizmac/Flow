using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// /search/status и /search/reindex при Search:Enabled=false — то есть в состоянии по умолчанию,
/// в котором приложение и живёт до появления модели эмбеддингов.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SearchApiTests(ApiFixture api)
{
    [Fact]
    public async Task Status_Requires_Token()
    {
        using var anonymous = api.CreateClient();

        using var response = await anonymous.GetAsync("/search/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_Reports_Disabled_Search_Without_Touching_Embedder()
    {
        using var owner = api.CreateClientAs();

        var status = await owner.GetFromJsonAsync<SearchStatusResponse>("/search/status");

        Assert.False(status!.Enabled);
        Assert.False(status.EmbedderAvailable);
        Assert.Equal(512, status.Dimensions);
        Assert.NotEmpty(status.ModelVersion);
        Assert.Equal(0, status.QueueTotal);
        Assert.Equal(0, status.ChunksByType.Task);
        Assert.Null(status.OldestQueuedAt);
    }

    [Fact]
    public async Task Status_Is_Forbidden_For_Developer()
    {
        using var owner = api.CreateClientAs();
        using var created = await owner.PostAsJsonAsync(
            "/users",
            new CreateUserRequest("search.dev", "search.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));

        var developer = (await created.Content.ReadFromJsonAsync<UserResponse>())!;
        using var client = api.CreateClientAs(developer.Id);

        using var response = await client.GetAsync("/search/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reindex_Is_Rejected_While_Search_Is_Disabled()
    {
        using var owner = api.CreateClientAs();

        using var response = await owner.PostAsJsonAsync("/search/reindex", new ReindexRequest());

        // Выключенный поиск — не 500 и не тихое согласие: Owner получает внятный отказ.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
