using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Служебные ручки поиска при дефолтной конфигурации репозитория (Search:Enabled=false).
/// Главное, что здесь проверяется: выключенный поиск отвечает честными нулями и не ломает ничего вокруг.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SearchApiTests(ApiFixture api)
{
    [Fact]
    public async Task Status_Should_Report_Disabled_Search_With_Zeroes()
    {
        using var owner = api.CreateClientAs();

        var status = await owner.GetFromJsonAsync<SearchStatusResponse>("/search/status");

        Assert.NotNull(status);
        Assert.False(status.Enabled);
        Assert.False(status.EmbedderAvailable);
        Assert.Null(status.ModelVersion);
        Assert.Equal(0, status.Dimensions);
        Assert.Equal(0, status.QueueTotal);
        Assert.Equal(0, status.QueueStuck);
        Assert.Null(status.OldestQueuedAt);
        Assert.Equal(new SearchChunkCountsResponse(0, 0, 0, 0), status.ChunksByType);
    }

    [Fact]
    public async Task Status_Should_Require_Token()
    {
        using var anonymous = api.CreateClient();

        using var response = await anonymous.GetAsync("/search/status");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Status_Should_Be_Forbidden_For_Member()
    {
        using var owner = api.CreateClientAs();
        using var created = await owner.PostAsJsonAsync("/users",
            new CreateUserRequest("search.member", "search.member@example.com", "A", "B", "correct horse battery", UserRole.Member));
        var member = (await created.Content.ReadFromJsonAsync<UserResponse>())!;

        using var asMember = api.CreateClientAs(member.Id);
        using var response = await asMember.GetAsync("/search/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Reindex_Should_Explain_That_Search_Is_Off()
    {
        using var owner = api.CreateClientAs();

        using var response = await owner.PostAsJsonAsync("/search/reindex", new ReindexRequest());

        // Молча наполнять очередь, которую никто не разберёт, хуже, чем сказать «поиск выключен».
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Search:Enabled", await response.Content.ReadAsStringAsync());
    }
}
