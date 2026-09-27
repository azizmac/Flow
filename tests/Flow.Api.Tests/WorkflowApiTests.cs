using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Workflow через HTTP (этап 3B): маршруты, коды и 400 с reasons при запрещённом переходе.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkflowApiTests(ApiFixture api)
{
    [Fact]
    public async Task Workflow_Routes_And_Refused_Transition()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Workflow", "WFA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        Guid Id(string name) => board.Statuses.Single(s => s.Name == name).Id;
        var (todo, doing, review, done) = (Id("Не начата"), Id("В работе"), Id("На проверке"), Id("Сделана"));

        using var deadEnds = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/workflow",
            new SetWorkflowRequest(WorkflowMode.Restricted, [new(todo, doing)]));
        Assert.Equal(HttpStatusCode.BadRequest, deadEnds.StatusCode);

        using var saved = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/workflow",
            new SetWorkflowRequest(WorkflowMode.Restricted, [new(todo, doing), new(doing, review), new(review, done), new(null, todo)]));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(WorkflowMode.Restricted, (await owner.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.WorkflowMode);

        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("T", null, null));
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>())!;

        var transitions = await owner.GetFromJsonAsync<List<TaskTransitionResponse>>($"/api/tasks/{task.Id}/transitions");
        Assert.True(transitions!.Single(t => t.StatusId == doing).Allowed);
        Assert.False(transitions.Single(t => t.StatusId == done).Allowed);

        using var refused = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(null, null, done));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("reasons").GetArrayLength());

        using var inStatus = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("Сразу", null, review));
        Assert.Equal(HttpStatusCode.BadRequest, inStatus.StatusCode);

        using var missing = await owner.GetAsync($"/api/tasks/{Guid.NewGuid()}/transitions");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
