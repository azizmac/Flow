using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Sprints;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Спринты и бэклог через HTTP (этап 2D): маршруты, коды ответов, разбор query-string бэклога.</summary>
[Collection(ApiCollection.Name)]
public sealed class SprintApiTests(ApiFixture api)
{
    [Fact]
    public async Task Sprint_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Спринты", "SPR"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("T", null, null));
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>())!;

        using var sprintResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/sprints", new CreateSprintRequest());
        var sprint = (await sprintResponse.Content.ReadFromJsonAsync<SprintResponse>())!;
        Assert.Equal("Спринт 1", sprint.Name);

        using var planned = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/sprint", new SetTaskSprintRequest(sprint.Id));
        Assert.Equal(sprint.Id, (await planned.Content.ReadFromJsonAsync<TaskResponse>())!.SprintId);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        using var badStart = await owner.PostAsJsonAsync($"/api/sprints/{sprint.Id}/start", new StartSprintRequest(today, today));
        Assert.Equal(HttpStatusCode.BadRequest, badStart.StatusCode);
        using var started = await owner.PostAsJsonAsync($"/api/sprints/{sprint.Id}/start", new StartSprintRequest(today, today.AddDays(14)));
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        using var deleteActive = await owner.DeleteAsync($"/api/sprints/{sprint.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteActive.StatusCode);

        var backlog = await owner.GetFromJsonAsync<BacklogResponse>($"/api/boards/{board.Id}/backlog?q=T");
        Assert.Equal(task.Id, Assert.Single(backlog!.Sections[0].Items).Task.Id);
        using var badFql = await owner.GetAsync($"/api/boards/{board.Id}/backlog?fql=sprint%20%3D");
        Assert.Equal(HttpStatusCode.BadRequest, badFql.StatusCode);

        using var completed = await owner.PostAsJsonAsync($"/api/sprints/{sprint.Id}/complete", new CompleteSprintRequest());
        Assert.Equal(SprintState.Completed, (await completed.Content.ReadFromJsonAsync<SprintResponse>())!.State);
        var report = await owner.GetFromJsonAsync<SprintReportResponse>($"/api/sprints/{sprint.Id}/report");
        Assert.Equal(1, report!.Committed.Count);
        Assert.Single(await owner.GetFromJsonAsync<List<SprintResponse>>($"/api/boards/{board.Id}/sprints") ?? []);

        using var missing = await owner.GetAsync($"/api/sprints/{Guid.NewGuid()}/report");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
