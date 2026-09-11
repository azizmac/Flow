using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Комментарии и журнал задачи через HTTP: 201 + Location, 403 для Reader, активность после PATCH.</summary>
[Collection(ApiCollection.Name)]
public sealed class TimelineApiTests(ApiFixture api)
{
    private static async Task<TaskResponse> CreateTaskAsync(HttpClient client, string key)
    {
        using var boardResponse = await client.PostAsJsonAsync("/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, boardResponse.StatusCode);
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var taskResponse = await client.PostAsJsonAsync($"/boards/{board.Id}/tasks", new CreateTaskRequest("Task", null, null));
        Assert.Equal(HttpStatusCode.Created, taskResponse.StatusCode);
        return (await taskResponse.Content.ReadFromJsonAsync<TaskResponse>())!;
    }

    [Fact]
    public async Task Post_Comment_Should_Return201_With_Location_And_Appear_In_List()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner, "TLA");

        using var created = await owner.PostAsJsonAsync($"/tasks/{task.Id}/comments", new CreateTaskCommentRequest("Первый!"));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains($"/tasks/{task.Id}/comments", created.Headers.Location!.ToString());
        var comment = (await created.Content.ReadFromJsonAsync<TaskCommentResponse>())!;
        Assert.Equal(ApiFixture.BootstrapId, comment.AuthorId);

        var list = await owner.GetFromJsonAsync<List<TaskCommentResponse>>($"/tasks/{task.Id}/comments");
        Assert.Equal(comment.Id, Assert.Single(list!).Id);
        var withCount = await owner.GetFromJsonAsync<TaskResponse>($"/tasks/{task.Id}");
        Assert.Equal(1, withCount!.CommentCount);
    }

    [Fact]
    public async Task Reader_Gets403_On_Comment_But_Can_Read()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner, "TLB");
        using var createUser = await owner.PostAsJsonAsync("/users", new CreateUserRequest("timeline.reader", "timeline.reader@example.com", "A", "B", "correct horse battery", UserRole.Reader));
        var reader = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;

        using var asReader = api.CreateClientAs(reader.Id);
        using var forbidden = await asReader.PostAsJsonAsync($"/tasks/{task.Id}/comments", new CreateTaskCommentRequest("нет"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var read = await asReader.GetAsync($"/tasks/{task.Id}/activity");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Activity_Should_Contain_Changes_After_Patch()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner, "TLC");

        using var patched = await owner.PatchAsJsonAsync($"/tasks/{task.Id}", new UpdateTaskRequest("Renamed", null, null));
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);
        using var due = await owner.PatchAsJsonAsync($"/tasks/{task.Id}/due-date", new SetTaskDueDateRequest(new DateOnly(2026, 10, 1)));
        Assert.Equal(HttpStatusCode.OK, due.StatusCode);
        Assert.Equal(new DateOnly(2026, 10, 1), (await due.Content.ReadFromJsonAsync<TaskResponse>())!.DueDate);

        var activity = (await owner.GetFromJsonAsync<List<TaskActivityResponse>>($"/tasks/{task.Id}/activity"))!;

        Assert.Equal(
            new[] { TaskActivityType.Created, TaskActivityType.TitleChanged, TaskActivityType.DueDateChanged },
            activity.Select(a => a.Type));
        Assert.Equal("2026-10-01", activity[2].NewValue);
    }

    [Fact]
    public async Task Empty_Comment_Should_Return400_And_Unknown_Task_404()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner, "TLD");

        using var empty = await owner.PostAsJsonAsync($"/tasks/{task.Id}/comments", new CreateTaskCommentRequest("   "));
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var missing = await owner.GetAsync($"/tasks/{Guid.NewGuid()}/comments");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Edit_And_Delete_Comment()
    {
        using var owner = api.CreateClientAs();
        var task = await CreateTaskAsync(owner, "TLE");
        using var created = await owner.PostAsJsonAsync($"/tasks/{task.Id}/comments", new CreateTaskCommentRequest("v1"));
        var comment = (await created.Content.ReadFromJsonAsync<TaskCommentResponse>())!;

        using var edited = await owner.PatchAsJsonAsync($"/comments/{comment.Id}", new UpdateTaskCommentRequest("v2"));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var editedComment = (await edited.Content.ReadFromJsonAsync<TaskCommentResponse>())!;
        Assert.Equal("v2", editedComment.Body);
        Assert.NotNull(editedComment.EditedAt);

        using var deleted = await owner.DeleteAsync($"/comments/{comment.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var again = await owner.DeleteAsync($"/comments/{comment.Id}");
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }
}
