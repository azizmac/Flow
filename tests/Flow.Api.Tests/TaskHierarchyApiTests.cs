using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Иерархия и ранг через HTTP (этап 1B): маршруты, коды ответов и разбор query-string (cascade, parentId, sort=Rank).
/// Правила уровней и журнал — в Flow.Application.Tests, SQL дерева — в Flow.Infrastructure.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TaskHierarchyApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> CreateBoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private static async Task<TaskResponse> CreateAsync(HttpClient client, BoardResponse board, TaskTypeKind kind, Guid? parentId = null)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/tasks",
            new CreateTaskRequest(kind.ToString(), null, null, board.TaskTypes.First(t => t.Kind == kind).Id, ParentId: parentId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Hierarchy_Routes_Should_Return_Expected_Codes()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "HAP");
        var epic = await CreateAsync(owner, board, TaskTypeKind.Epic);
        var story = await CreateAsync(owner, board, TaskTypeKind.Story, epic.Id);
        var loose = await CreateAsync(owner, board, TaskTypeKind.Story);
        Assert.Equal(epic.Id, story.ParentId);

        using var badCreate = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks",
            new CreateTaskRequest("x", null, null, board.TaskTypes.First(t => t.Kind == TaskTypeKind.Epic).Id, ParentId: story.Id));
        Assert.Equal(HttpStatusCode.BadRequest, badCreate.StatusCode);

        using var reparent = await owner.PatchAsJsonAsync($"/api/tasks/{loose.Id}/parent", new SetTaskParentRequest(epic.Id));
        Assert.Equal(HttpStatusCode.OK, reparent.StatusCode);
        using var wrongLevel = await owner.PatchAsJsonAsync($"/api/tasks/{epic.Id}/parent", new SetTaskParentRequest(story.Id));
        Assert.Equal(HttpStatusCode.BadRequest, wrongLevel.StatusCode);
        using var missing = await owner.PatchAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/parent", new SetTaskParentRequest(null));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var rank = await owner.PostAsJsonAsync($"/api/tasks/{loose.Id}/rank", new RankTaskRequest(null, story.Id));
        Assert.Equal(HttpStatusCode.OK, rank.StatusCode);
        using var noNeighbours = await owner.PostAsJsonAsync($"/api/tasks/{loose.Id}/rank", new RankTaskRequest(null, null));
        Assert.Equal(HttpStatusCode.BadRequest, noNeighbours.StatusCode);

        var children = await owner.GetFromJsonAsync<TaskListResponse>($"/api/tasks?boardId={board.Id}&parentId={epic.Id}&sort=Rank&offset=0");
        Assert.Equal([loose.Id, story.Id], children!.Items.Select(t => t.Id));

        var tree = await owner.GetFromJsonAsync<List<TaskTreeNode>>($"/api/boards/{board.Id}/tree?rootId={epic.Id}");
        Assert.Equal([(epic.Id, 0), (loose.Id, 1), (story.Id, 1)], tree!.Select(n => (n.Task.Id, n.Depth)));
        using var noTree = await owner.GetAsync($"/api/boards/{Guid.NewGuid()}/tree");
        Assert.Equal(HttpStatusCode.NotFound, noTree.StatusCode);

        using var refused = await owner.DeleteAsync($"/api/tasks/{epic.Id}");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var cascaded = await owner.DeleteAsync($"/api/tasks/{epic.Id}?cascade=true");
        Assert.Equal(HttpStatusCode.NoContent, cascaded.StatusCode);
        using var gone = await owner.GetAsync($"/api/tasks/{story.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
