using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Календарь через HTTP (этап 2F): разбор дат в query-string, 400 на слишком широкое окно, 404 на чужой проект.</summary>
[Collection(ApiCollection.Name)]
public sealed class CalendarApiTests(ApiFixture api)
{
    [Fact]
    public async Task Calendar_Route_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Календарь", "CALA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;

        var ok = await owner.GetFromJsonAsync<TaskCalendarResponse>($"/api/tasks/calendar?from=2026-10-01&to=2026-10-31&boardId={board.Id}");
        Assert.Equal(new DateOnly(2026, 10, 1), ok!.From);

        using var wide = await owner.GetAsync("/api/tasks/calendar?from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.BadRequest, wide.StatusCode);
        using var missing = await owner.GetAsync($"/api/tasks/calendar?from=2026-10-01&to=2026-10-31&boardId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
