using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Этап 1A через HTTP: только то, чего нет ниже — маршруты, коды ответов и разбор query-string.
/// Правила (журнал, права, диапазоны) проверены в Flow.Application.Tests, SQL — в Flow.Infrastructure.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TaskPlanningApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> CreateBoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, Guid boardId, CreateTaskRequest request)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/tasks", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Board_Should_CarryTaskTypes_And_CreateTask_Should_AcceptTypeAndPriority()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PLA");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);

        var task = await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Баг", null, null, bug.Id, TaskPriority.High));

        Assert.Equal(5, board.TaskTypes.Count);
        Assert.Equal(bug.Id, task.TypeId);
        Assert.Equal(TaskPriority.High, task.Priority);
    }

    [Fact]
    public async Task Patch_Schedule_And_Estimate_Should_Return200_Or400()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PLB");
        var task = await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Задача", null, null));

        using var ok = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/schedule", new SetTaskScheduleRequest(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 1), (await ok.Content.ReadFromJsonAsync<TaskResponse>())!.StartDate);

        using var reversed = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/schedule", new SetTaskScheduleRequest(new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        using var dueBeforeStart = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/due-date", new SetTaskDueDateRequest(new DateOnly(2026, 9, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, dueBeforeStart.StatusCode);

        using var estimate = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/estimate", new SetTaskEstimateRequest(2.5m, 90));
        Assert.Equal(HttpStatusCode.OK, estimate.StatusCode);

        using var invalid = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/estimate", new SetTaskEstimateRequest(1.25m, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        using var missing = await owner.PatchAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/estimate", new SetTaskEstimateRequest(null, null));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Patch_Task_Should_Return400_ForArchivedOrForeignType()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PLC");
        var other = await CreateBoardAsync(owner, "PLD");
        var task = await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Задача", null, null));
        var epic = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Epic);
        using var archive = await owner.PatchAsJsonAsync($"/api/boards/{board.Id}/task-types/{epic.Id}", new UpdateTaskTypeRequest(IsArchived: true));
        Assert.Equal(HttpStatusCode.OK, archive.StatusCode);

        using var archived = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(null, null, null, epic.Id));
        using var foreign = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest(null, null, null, other.TaskTypes[0].Id));

        Assert.Equal(HttpStatusCode.BadRequest, archived.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
    }

    [Fact]
    public async Task TaskType_Endpoints_Should_Return200_400_404()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PLE");

        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/task-types", new CreateTaskTypeRequest("Инцидент", TaskTypeKind.Bug));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains((await created.Content.ReadFromJsonAsync<BoardResponse>())!.TaskTypes, t => t.Name == "Инцидент");

        using var duplicate = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/task-types", new CreateTaskTypeRequest("инцидент", TaskTypeKind.Bug));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var unknownKind = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/task-types", new CreateTaskTypeRequest("Странный", (TaskTypeKind)42));
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);

        var @default = board.TaskTypes.Single(t => t.IsDefault);
        using var archiveDefault = await owner.PatchAsJsonAsync($"/api/boards/{board.Id}/task-types/{@default.Id}", new UpdateTaskTypeRequest(IsArchived: true));
        Assert.Equal(HttpStatusCode.BadRequest, archiveDefault.StatusCode);

        using var missingBoard = await owner.PostAsJsonAsync($"/api/boards/{Guid.NewGuid()}/task-types", new CreateTaskTypeRequest("X", TaskTypeKind.Task));
        Assert.Equal(HttpStatusCode.NotFound, missingBoard.StatusCode);
    }

    [Fact]
    public async Task Get_Tasks_Should_ParseTypeKindPriorityAndSort_FromQueryString()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PLF");
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug);
        var low = await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Низкий", null, null, bug.Id, TaskPriority.Low));
        var high = await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Высокий", null, null, bug.Id, TaskPriority.High));
        await CreateTaskAsync(owner, board.Id, new CreateTaskRequest("Не баг", null, null, null, TaskPriority.High));

        var bugs = await owner.GetFromJsonAsync<TaskListResponse>(
            $"/api/tasks?boardId={board.Id}&typeKind=Bug&sort=Priority&dir=desc&offset=0");
        Assert.Equal([high.Id, low.Id], bugs!.Items.Select(t => t.Id));

        var highOnly = await owner.GetFromJsonAsync<TaskListResponse>($"/api/tasks?boardId={board.Id}&priority=High&typeKind=Bug");
        Assert.Equal(high.Id, Assert.Single(highOnly!.Items).Id);

        using var unknown = await owner.GetAsync($"/api/tasks?boardId={board.Id}&priority=42");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
