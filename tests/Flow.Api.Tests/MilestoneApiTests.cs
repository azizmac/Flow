using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Milestones;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Вехи через HTTP (этап 2E): маршруты и коды ответов; правила — в Application и Infrastructure тестах.</summary>
[Collection(ApiCollection.Name)]
public sealed class MilestoneApiTests(ApiFixture api)
{
    [Fact]
    public async Task Milestone_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Вехи", "MLS"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("T", null, null));
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>())!;

        using var milestoneResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/milestones", new CreateMilestoneRequest("1.0"));
        var milestone = (await milestoneResponse.Content.ReadFromJsonAsync<MilestoneResponse>())!;
        using var duplicate = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/milestones", new CreateMilestoneRequest("1.0"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var put = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/milestone", new SetTaskMilestoneRequest(milestone.Id));
        Assert.Equal(milestone.Id, (await put.Content.ReadFromJsonAsync<TaskResponse>())!.MilestoneId);

        using var closed = await owner.PatchAsJsonAsync($"/api/milestones/{milestone.Id}", new UpdateMilestoneRequest(Closed: true));
        var closedMilestone = (await closed.Content.ReadFromJsonAsync<MilestoneResponse>())!;
        Assert.Equal((MilestoneState.Closed, 1), (closedMilestone.State, closedMilestone.Progress.Total));
        Assert.Single(await owner.GetFromJsonAsync<List<MilestoneResponse>>($"/api/boards/{board.Id}/milestones") ?? []);

        var byFql = await owner.GetFromJsonAsync<TaskListResponse>($"/api/tasks?boardId={board.Id}&fql=milestone%20%3D%20%221.0%22");
        Assert.Equal(task.Id, Assert.Single(byFql!.Items).Id);

        using var deleted = await owner.DeleteAsync($"/api/milestones/{milestone.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await owner.GetAsync($"/api/milestones/{milestone.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    /// <summary>Этап 2H: PUT /milestones/{id}/boards и стрелки роадмапа GET /boards/{id}/blocks.</summary>
    [Fact]
    public async Task Share_And_Board_Blocks_Routes()
    {
        using var owner = api.CreateClientAs();
        async Task<BoardResponse> Board(string key)
        {
            using var response = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Проект " + key, key));
            return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
        }

        var front = await Board("SHAF");
        var back = await Board("SHAB");
        using var milestoneResponse = await owner.PostAsJsonAsync($"/api/boards/{front.Id}/milestones", new CreateMilestoneRequest("Общий релиз"));
        var milestone = (await milestoneResponse.Content.ReadFromJsonAsync<MilestoneResponse>())!;

        using var shared = await owner.PutAsJsonAsync($"/api/milestones/{milestone.Id}/boards", new ShareMilestoneRequest([back.Id]));
        Assert.Equal([back.Id], (await shared.Content.ReadFromJsonAsync<MilestoneResponse>())!.SharedBoardIds);
        using var missing = await owner.PutAsJsonAsync($"/api/milestones/{Guid.NewGuid()}/boards", new ShareMilestoneRequest([]));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        Assert.Empty(await owner.GetFromJsonAsync<List<TaskBlockEdge>>($"/api/boards/{front.Id}/blocks") ?? [null!]);
        using var hidden = await owner.GetAsync($"/api/boards/{Guid.NewGuid()}/blocks");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
