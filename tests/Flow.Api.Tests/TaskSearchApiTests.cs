using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Сводный список задач через HTTP: отбор по всем проектам и по одному, фильтр по типу статуса, доступ Reader.</summary>
[Collection(ApiCollection.Name)]
public sealed class TaskSearchApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> CreateBoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, Guid boardId, string title, Guid? statusId = null)
    {
        using var response = await client.PostAsJsonAsync($"/api/boards/{boardId}/tasks", new CreateTaskRequest(title, null, statusId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Get_Tasks_Should_SpanBoards_And_Narrow_ByBoard()
    {
        using var owner = api.CreateClientAs();
        var first = await CreateBoardAsync(owner, "SRA");
        var second = await CreateBoardAsync(owner, "SRB");
        var here = await CreateTaskAsync(owner, first.Id, "Здесь");
        var there = await CreateTaskAsync(owner, second.Id, "Там");

        var all = await owner.GetFromJsonAsync<TaskListResponse>("/api/tasks?limit=500");
        Assert.Contains(all!.Items, t => t.Id == here.Id);
        Assert.Contains(all.Items, t => t.Id == there.Id);

        var onlyFirst = await owner.GetFromJsonAsync<TaskListResponse>($"/api/tasks?boardId={first.Id}");
        Assert.Equal(here.Id, Assert.Single(onlyFirst!.Items).Id);
        Assert.Equal(1, onlyFirst.Total);
        Assert.Null(onlyFirst.NextCursor);
    }

    [Fact]
    public async Task Get_Tasks_Should_FilterByStatusType_FromQueryString()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "SRC");
        var done = board.Statuses.First(s => s.Type == StatusType.Done);
        var finished = await CreateTaskAsync(owner, board.Id, "Готово", done.Id);
        await CreateTaskAsync(owner, board.Id, "Не начата");

        var result = await owner.GetFromJsonAsync<TaskListResponse>($"/api/tasks?boardId={board.Id}&statusType={StatusType.Done}");

        Assert.Equal(finished.Id, Assert.Single(result!.Items).Id);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.ByType.Single(c => c.Type == StatusType.Done).Count);
        Assert.Equal(1, result.ByStatus.Single(c => c.StatusId == done.Id).Count);
    }

    [Fact]
    public async Task Reader_Can_Read_Task_List()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "SRD");
        await CreateTaskAsync(owner, board.Id, "Видна читателю");
        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("search.reader", "search.reader@example.com", "A", "B", "correct horse battery", UserRole.Reader));
        var reader = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;

        using var asReader = api.CreateClientAs(reader.Id);
        using var response = await asReader.GetAsync($"/api/tasks?boardId={board.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = (await response.Content.ReadFromJsonAsync<TaskListResponse>())!;
        Assert.Single(list.Items);
    }
}
