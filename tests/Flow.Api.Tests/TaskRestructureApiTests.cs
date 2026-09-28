using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Слияние, разделение, перенос через HTTP (этап 1E): маршруты и коды — 201 на разделение, 400 на пустое разделение
/// и слияние с собой, превью POST'ом с телом, перенос и поиск по прежнему коду, 404 на неизвестный код.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TaskRestructureApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> BoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Проект {key}", key));
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private static async Task<TaskResponse> TaskAsync(HttpClient client, Guid boardId, string title)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/tasks", new CreateTaskRequest(title, null, null));
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Restructure_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        var source = await BoardAsync(owner, "RSA");
        var target = await BoardAsync(owner, "RSB");
        var first = await TaskAsync(owner, source.Id, "Первая");
        var second = await TaskAsync(owner, source.Id, "Вторая");

        using var split = await owner.PostAsJsonAsync($"/api/tasks/{first.Id}/split", new SplitTaskRequest([new SplitPart("Часть")]));
        Assert.Equal(HttpStatusCode.Created, split.StatusCode);
        using var emptySplit = await owner.PostAsJsonAsync($"/api/tasks/{first.Id}/split", new SplitTaskRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, emptySplit.StatusCode);

        using var self = await owner.PostAsJsonAsync($"/api/tasks/{first.Id}/merge", new MergeTaskRequest(first.Id));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var merge = await owner.PostAsJsonAsync($"/api/tasks/{second.Id}/merge", new MergeTaskRequest(first.Id));
        Assert.Equal(HttpStatusCode.OK, merge.StatusCode);

        using var preview = await owner.PostAsJsonAsync($"/api/tasks/{first.Id}/move/preview", new MoveTaskRequest(target.Id));
        Assert.Equal(1, (await preview.Content.ReadFromJsonAsync<TaskMovePreviewResponse>())!.TaskCount);

        using var move = await owner.PostAsJsonAsync($"/api/tasks/{first.Id}/move", new MoveTaskRequest(target.Id));
        Assert.Equal("RSB-1", (await move.Content.ReadFromJsonAsync<TaskResponse>())!.Code);

        var byOldCode = await owner.GetFromJsonAsync<TaskResponse>($"/api/tasks/by-code/{first.Code}");
        Assert.Equal(first.Id, byOldCode!.Id);
        using var unknown = await owner.GetAsync("/api/tasks/by-code/NOPE-1");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }
}
