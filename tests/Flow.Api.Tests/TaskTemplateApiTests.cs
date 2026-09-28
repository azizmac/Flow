using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Шаблоны задач через HTTP (этап 3G): маршруты и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class TaskTemplateApiTests(ApiFixture api)
{
    [Fact]
    public async Task Task_Template_Routes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Шаблоны", "TTA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/task-templates",
            new SaveTaskTemplateRequest("Баг", "Баг: ", Checklist: ["Воспроизвести"], Subtasks: [new("Исправить")]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = (await created.Content.ReadFromJsonAsync<TaskTemplateResponse>())!;
        using var invalid = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/task-templates", new SaveTaskTemplateRequest("", "X"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Single((await owner.GetFromJsonAsync<List<TaskTemplateResponse>>($"/api/boards/{board.Id}/task-templates"))!);
        using var missingBoard = await owner.GetAsync($"/api/boards/{Guid.NewGuid()}/task-templates");
        Assert.Equal(HttpStatusCode.NotFound, missingBoard.StatusCode);

        using var taskResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks",
            new CreateTaskRequest("Баг: вход", null, null, TemplateId: template.Id));
        Assert.Equal(HttpStatusCode.Created, taskResponse.StatusCode);
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskResponse>())!;
        Assert.Equal(1, task.ChecklistTotal);

        using var saved = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/save-as-template", new TaskTemplateFromTaskRequest("Из задачи"));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        using var updated = await owner.PutAsJsonAsync($"/api/task-templates/{template.Id}", new SaveTaskTemplateRequest("Баг", "Баг {n}"));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        using var missing = await owner.PutAsJsonAsync($"/api/task-templates/{Guid.NewGuid()}", new SaveTaskTemplateRequest("X", "X"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var deleted = await owner.DeleteAsync($"/api/task-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
