using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Dashboards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Дашборды через HTTP (этап 2G): маршруты и коды — 201 на создание, 204 без дашборда по умолчанию, 400 на битый
/// FQL виджета, 200 с error на данные сломанного виджета, 403 чужому автору, 404 на личный чужой дашборд.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DashboardApiTests(ApiFixture api)
{
    [Fact]
    public async Task Dashboard_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var userResponse = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("dash.member", "dash.member@example.com", "A", "B", "correct horse battery", UserRole.Member));
        var member = (await userResponse.Content.ReadFromJsonAsync<UserResponse>())!;
        using var asMember = api.CreateClientAs(member.Id);

        using var none = await asMember.GetAsync("/api/dashboards/default");
        Assert.Equal(HttpStatusCode.NoContent, none.StatusCode);

        using var created = await owner.PostAsJsonAsync("/api/dashboards", new CreateDashboardRequest("Общий", Shared: true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var dashboard = (await created.Content.ReadFromJsonAsync<DashboardResponse>())!;

        using var badFql = await owner.PostAsJsonAsync($"/api/dashboards/{dashboard.Id}/widgets",
            new AddWidgetRequest(WidgetType.Counter, null, new WidgetConfig(Fql: "status =")));
        Assert.Equal(HttpStatusCode.BadRequest, badFql.StatusCode);

        using var added = await owner.PostAsJsonAsync($"/api/dashboards/{dashboard.Id}/widgets",
            new AddWidgetRequest(WidgetType.SprintBurndown, "Спринт", new WidgetConfig(BoardId: Guid.NewGuid())));
        var widget = (await added.Content.ReadFromJsonAsync<DashboardResponse>())!.Widgets.Single();

        var data = await asMember.GetFromJsonAsync<WidgetDataResponse>($"/api/dashboards/{dashboard.Id}/widgets/{widget.Id}/data");
        Assert.NotNull(data!.Error);

        using var foreign = await asMember.PatchAsJsonAsync($"/api/dashboards/{dashboard.Id}", new UpdateDashboardRequest(Name: "Моё"));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var personal = await owner.PostAsJsonAsync("/api/dashboards", new CreateDashboardRequest("Личный"));
        var hidden = (await personal.Content.ReadFromJsonAsync<DashboardResponse>())!;
        using var notFound = await asMember.GetAsync($"/api/dashboards/{hidden.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }
}
