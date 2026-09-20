using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// HTTP-поверхность поиска: права, коды ответов и деградация при недоступной модели (в фикстуре
/// эмбеддер указывает в никуда — см. ApiFixture). Качество выдачи проверяют тесты Infrastructure.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class SearchApiTests(ApiFixture api)
{
    [Fact]
    public async Task Search_Requires_Token()
    {
        using var anonymous = api.CreateClient();

        using var response = await anonymous.GetAsync("/api/search?q=что-нибудь");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

     [Fact]
    public async Task Search_Rejects_Unknown_Source_Type()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync("/api/search?q=экспорт&types=task,sprint");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_Is_Available_To_Any_Role()
    {
        using var owner = api.CreateClientAs();
        using var created = await owner.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest("search.reader", "search.reader@example.com", "A", "B", "correct horse battery", UserRole.Reader));

        var reader = (await created.Content.ReadFromJsonAsync<UserResponse>())!;
        using var client = api.CreateClientAs(reader.Id);

        using var response = await client.GetAsync("/api/search?q=экспорт");

        // Читать может любая роль — как и остальные запросы (docs/TZ_user_roles.md).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Status_Is_Reported_For_Enabled_Search()
    {
        using var owner = api.CreateClientAs();

        var status = await owner.GetFromJsonAsync<SearchStatusResponse>("/api/search/status");

        Assert.True(status!.Enabled);
        Assert.False(status.EmbedderAvailable);
        Assert.Equal(512, status.Dimensions);
        Assert.NotEmpty(status.ModelVersion);
    }

    [Fact]
    public async Task Status_Is_Forbidden_For_Developer()
    {
        using var owner = api.CreateClientAs();
        using var created = await owner.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest("search.dev", "search.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));

        var developer = (await created.Content.ReadFromJsonAsync<UserResponse>())!;
        using var client = api.CreateClientAs(developer.Id);

        using var response = await client.GetAsync("/api/search/status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Similar_Requires_Token()
    {
        using var anonymous = api.CreateClient();

        using var response = await anonymous.GetAsync($"/api/tasks/{Guid.NewGuid()}/similar");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Similar_Is_Not_Found_For_Unknown_Task()
    {
        using var client = api.CreateClientAs();

        using var response = await client.GetAsync($"/api/tasks/{Guid.NewGuid()}/similar");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Similar_Is_Empty_While_The_Task_Is_Not_Indexed()
    {
        using var client = api.CreateClientAs();
        using var board = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Похожие", "SIM"));
        var created = (await board.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var task = await client.PostAsJsonAsync($"/api/boards/{created.Id}/tasks", new CreateTaskRequest("Задача без индекса", null, null));
        var response = (await task.Content.ReadFromJsonAsync<TaskResponse>())!;

        // Воркер в тестах выключен: чанков ещё нет, и блок «похожие» должен быть пустым, а не 500.
        var similar = await client.GetFromJsonAsync<IReadOnlyList<SearchResultItem>>($"/api/tasks/{response.Id}/similar");

        Assert.Empty(similar!);
    }

    [Fact]
    public async Task Search_Reports_Recognized_Filters()
    {
        using var client = api.CreateClientAs();

        var response = await client.GetFromJsonAsync<SearchResponse>("/api/search?q=мои просроченные экспорт");

        // Фильтры разбираются без модели, поэтому видны даже при погашенном эмбеддере.
        Assert.Contains("мои", response!.Intent.Filters);
        Assert.Contains("просроченные", response.Intent.Filters);
        Assert.Equal("экспорт", response.Intent.Text);
    }

    [Fact]
    public async Task Reindex_Is_Accepted_For_Owner()
    {
        using var owner = api.CreateClientAs();

        using var response = await owner.PostAsJsonAsync("/api/search/reindex", new ReindexRequest());

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }
}
