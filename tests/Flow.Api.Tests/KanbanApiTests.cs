using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Канбан и дерево через HTTP (этапы 2B, 2C): маршруты, разбор query-string и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class KanbanApiTests(ApiFixture api)
{
    [Fact]
    public async Task Board_Routes_And_Status_Move()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Канбан", "KBA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        var doing = board.Statuses.Single(s => s.Name == "В работе").Id;

        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("T", null, null));
        var task = (await created.Content.ReadFromJsonAsync<TaskResponse>())!;

        var kanban = await owner.GetFromJsonAsync<TaskBoardResponse>($"/api/tasks/board?boardId={board.Id}");
        Assert.Equal(board.Statuses.Count, kanban!.Columns.Count);

        using var moved = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/rank", new RankTaskRequest(null, null, doing));
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);

        // Запрещённый workflow перенос — 400 с причинами, а не 200 с пустым телом.
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        using var workflow = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/workflow",
            new SetWorkflowRequest(WorkflowMode.Restricted, [new(null, doing), new(doing, board.Statuses.First().Id)]));
        Assert.Equal(HttpStatusCode.OK, workflow.StatusCode);
        using var refused = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/rank", new RankTaskRequest(null, null, done));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("reasons", await refused.Content.ReadAsStringAsync());

        var column = await owner.GetFromJsonAsync<TaskBoardResponse>($"/api/tasks/board?boardId={board.Id}&statusId={doing}&offset=0");
        Assert.Equal(task.Id, Assert.Single(Assert.Single(column!.Columns).Tasks).Id);

        var other = await owner.GetFromJsonAsync<TaskBoardResponse>("/api/tasks/board?other=true");
        Assert.True(Assert.Single(other!.Columns).Other);

        using var unknownType = await owner.GetAsync("/api/tasks/board?statusType=42");
        Assert.Equal(HttpStatusCode.BadRequest, unknownType.StatusCode);
        using var badFql = await owner.GetAsync("/api/tasks/board?fql=status%20%3D");
        Assert.Equal(HttpStatusCode.BadRequest, badFql.StatusCode);
        using var missing = await owner.GetAsync($"/api/tasks/board?boardId={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // Дерево того же проекта: фильтр и глубина разбираются из query-string.
        var tree = await owner.GetFromJsonAsync<List<TaskTreeNode>>($"/api/boards/{board.Id}/tree?q=T&maxDepth=1");
        Assert.Equal(task.Id, Assert.Single(tree!).Task.Id);
        using var badDepth = await owner.GetAsync($"/api/boards/{board.Id}/tree?maxDepth=-1");
        Assert.Equal(HttpStatusCode.BadRequest, badDepth.StatusCode);
        using var badTreeFql = await owner.GetAsync($"/api/boards/{board.Id}/tree?fql=status%20%3D");
        Assert.Equal(HttpStatusCode.BadRequest, badTreeFql.StatusCode);

        using var window = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/done-column-days", new SetDoneColumnDaysRequest(30));
        Assert.Equal(30, (await window.Content.ReadFromJsonAsync<BoardResponse>())!.DoneColumnDays);
        using var badWindow = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/done-column-days", new SetDoneColumnDaysRequest(0));
        Assert.Equal(HttpStatusCode.BadRequest, badWindow.StatusCode);
    }
}
