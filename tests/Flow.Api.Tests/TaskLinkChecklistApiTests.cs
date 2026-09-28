using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Связи и чек-лист через HTTP (этап 1C): маршруты и коды ответов (201/400/404/409). Правила — в Flow.Application.Tests,
/// SQL и каскады — в Flow.Infrastructure.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TaskLinkChecklistApiTests(ApiFixture api)
{
    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, Guid boardId, string title)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/tasks", new CreateTaskRequest(title, null, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Link_Routes_Should_Return_Expected_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Links", "LNA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        var a = await CreateTaskAsync(owner, board.Id, "A");
        var b = await CreateTaskAsync(owner, board.Id, "B");

        using var created = await owner.PostAsJsonAsync($"/api/tasks/{a.Id}/links", new CreateTaskLinkRequest(TaskLinkType.Blocks, TargetCode: b.Code));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var link = (await created.Content.ReadFromJsonAsync<TaskLinkCreatedResponse>())!.Link;

        using var duplicate = await owner.PostAsJsonAsync($"/api/tasks/{a.Id}/links", new CreateTaskLinkRequest(TaskLinkType.Blocks, b.Id));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var self = await owner.PostAsJsonAsync($"/api/tasks/{a.Id}/links", new CreateTaskLinkRequest(TaskLinkType.Blocks, a.Id));
        Assert.Equal(HttpStatusCode.BadRequest, self.StatusCode);
        using var noTask = await owner.PostAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/links", new CreateTaskLinkRequest(TaskLinkType.Blocks, b.Id));
        Assert.Equal(HttpStatusCode.NotFound, noTask.StatusCode);

        var inward = await owner.GetFromJsonAsync<List<TaskLinkResponse>>($"/api/tasks/{b.Id}/links");
        Assert.False(Assert.Single(inward!).Outward);
        Assert.Equal(1, (await owner.GetFromJsonAsync<TaskResponse>($"/api/tasks/{b.Id}"))!.BlockedByCount);

        using var deleted = await owner.DeleteAsync($"/api/links/{link.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await owner.DeleteAsync($"/api/links/{link.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task Checklist_Routes_Should_Return_Expected_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Checklist", "CLA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        var task = await CreateTaskAsync(owner, board.Id, "T");

        using var added = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/checklist", new AddChecklistItemRequest("Пункт"));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var item = Assert.Single((await added.Content.ReadFromJsonAsync<List<TaskChecklistItemResponse>>())!);

        using var empty = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/checklist", new AddChecklistItemRequest(" "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var toggled = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/checklist/{item.Id}", new UpdateChecklistItemRequest(IsDone: true));
        Assert.True(Assert.Single((await toggled.Content.ReadFromJsonAsync<List<TaskChecklistItemResponse>>())!).IsDone);

        using var badOrder = await owner.PutAsJsonAsync($"/api/tasks/{task.Id}/checklist/order", new ReorderChecklistRequest([Guid.NewGuid()]));
        Assert.Equal(HttpStatusCode.BadRequest, badOrder.StatusCode);

        using var removed = await owner.DeleteAsync($"/api/tasks/{task.Id}/checklist/{item.Id}");
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var missingTask = await owner.GetAsync($"/api/tasks/{Guid.NewGuid()}/checklist");
        Assert.Equal(HttpStatusCode.NotFound, missingTask.StatusCode);
    }
}
