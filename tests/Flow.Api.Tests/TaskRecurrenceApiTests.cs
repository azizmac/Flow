using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Повторение через HTTP (этап 1F): 404 без правила, PUT 200/400, превью GET и POST, DELETE 204.</summary>
[Collection(ApiCollection.Name)]
public sealed class TaskRecurrenceApiTests(ApiFixture api)
{
    [Fact]
    public async Task Recurrence_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Повторения", "RCAP"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var taskResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("Отчёт", null, null));
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskResponse>())!;
        var url = $"/api/tasks/{task.Id}/recurrence";

        using var none = await owner.GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);

        using var invalid = await owner.PutAsJsonAsync(url, new TaskRecurrenceRequest(RecurrenceFrequency.Daily, 0, new DateOnly(2030, 1, 1)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var rule = new TaskRecurrenceRequest(RecurrenceFrequency.Monthly, 1, new DateOnly(2030, 1, 15), MonthDay: -1);
        using var draft = await owner.PostAsJsonAsync($"{url}/preview?count=2", rule);
        Assert.Equal([new DateOnly(2030, 1, 31), new DateOnly(2030, 2, 28)], await draft.Content.ReadFromJsonAsync<List<DateOnly>>());

        using var saved = await owner.PutAsJsonAsync(url, rule);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(3, (await owner.GetFromJsonAsync<List<DateOnly>>($"{url}/preview?count=3"))!.Count);

        using var deleted = await owner.DeleteAsync(url);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
