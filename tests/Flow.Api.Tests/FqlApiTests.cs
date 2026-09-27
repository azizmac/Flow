using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Filters;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// FQL и сохранённые фильтры через HTTP (этап 2A): 400 с позицией, разбор ?fql= и ?pos=, коды /filters.
/// Грамматика и биндинг — в Flow.Application.Tests, трансляция — в Flow.Infrastructure.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class FqlApiTests(ApiFixture api)
{
    [Fact]
    public async Task Broken_Fql_Should_Return_400_With_Position()
    {
        using var owner = api.CreateClientAs();

        using var ok = await owner.GetAsync("/api/tasks?offset=0&fql=" + Uri.EscapeDataString("priority >= high ORDER BY due DESC"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);

        using var broken = await owner.GetAsync("/api/tasks?fql=" + Uri.EscapeDataString("status = "));
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        var error = (await broken.Content.ReadFromJsonAsync<FqlErrorResponse>())!;
        Assert.Equal(9, error.Position);
        Assert.False(string.IsNullOrEmpty(error.Message));
    }

    [Fact]
    public async Task Suggest_Should_Read_Cursor_From_Query_String()
    {
        using var owner = api.CreateClientAs();

        var fields = await owner.GetFromJsonAsync<FqlSuggestResponse>("/api/tasks/query/suggest?q=" + Uri.EscapeDataString("prio") + "&pos=4");
        Assert.Equal("priority", fields!.Items.First().Label);

        var values = await owner.GetFromJsonAsync<FqlSuggestResponse>("/api/tasks/query/suggest?q=" + Uri.EscapeDataString("priority = "));
        Assert.Contains(values!.Items, i => i.Insert == "critical");
    }

    [Fact]
    public async Task Filters_Should_Return_Expected_Codes()
    {
        using var owner = api.CreateClientAs();
        using var userResponse = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("fql.member", "fql.member@example.com", "A", "B", "correct horse battery", UserRole.Member));
        var member = (await userResponse.Content.ReadFromJsonAsync<UserResponse>())!;
        using var asMember = api.CreateClientAs(member.Id);

        using var created = await owner.PostAsJsonAsync("/api/filters", new CreateSavedFilterRequest("Срочные", "priority >= high", Shared: true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var filter = (await created.Content.ReadFromJsonAsync<SavedFilterResponse>())!;

        using var broken = await owner.PostAsJsonAsync("/api/filters", new CreateSavedFilterRequest("Битый", "status IN (a"));
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Equal(12, (await broken.Content.ReadFromJsonAsync<FqlErrorResponse>())!.Position);

        using var personal = await owner.PostAsJsonAsync("/api/filters", new CreateSavedFilterRequest("Личный", ""));
        var hidden = (await personal.Content.ReadFromJsonAsync<SavedFilterResponse>())!;
        using var notVisible = await asMember.GetAsync($"/api/filters/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notVisible.StatusCode);

        using var forbidden = await asMember.PatchAsJsonAsync($"/api/filters/{filter.Id}", new UpdateSavedFilterRequest(Name: "Моё"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var star = await asMember.PutAsync($"/api/filters/{filter.Id}/star", null);
        Assert.True((await star.Content.ReadFromJsonAsync<SavedFilterResponse>())!.IsStarred);

        using var deleted = await owner.DeleteAsync($"/api/filters/{filter.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
