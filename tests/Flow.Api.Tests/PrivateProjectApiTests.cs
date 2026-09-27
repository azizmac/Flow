using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Flow.Shared.Contracts.Attachments;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Приватный проект через HTTP (этап 4B): не участнику — 404 и на чтение, и на изменение, и на прямую ссылку
/// /files/{id}; маршрут видимости и его коды. Правила — в Flow.Application.Tests (PrivateProjectTests).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class PrivateProjectApiTests(ApiFixture api)
{
    [Fact]
    public async Task Hidden_Project_Is_404_Everywhere_For_Non_Member()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Секретный", "PPA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var taskResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("Скрытая", null, null));
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskResponse>())!;
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("секрет"));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = await owner.PostAsync($"/api/tasks/{task.Id}/attachments", new MultipartFormDataContent { { file, "file", "секрет.txt" } });
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentResponse>())!;

        using var privateResponse = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/visibility", new SetVisibilityRequest(BoardVisibility.Private));
        Assert.Equal(HttpStatusCode.OK, privateResponse.StatusCode);
        Assert.Equal(BoardVisibility.Private, (await privateResponse.Content.ReadFromJsonAsync<BoardMembersResponse>())!.Visibility);

        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("ppa.dev", "ppa.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));
        var developer = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;
        using var outsider = api.CreateClientAs(developer.Id);

        foreach (var url in new[]
                 {
                     $"/api/boards/{board.Id}", $"/api/tasks/{task.Id}", $"/api/tasks/{task.Id}/comments",
                     $"/api/tasks/{task.Id}/activity", $"/api/tasks/{task.Id}/attachments", $"/api/boards/{board.Id}/members",
                     $"/api/boards/{board.Id}/my-access", $"/api/attachments/{attachment.Id}/content", $"/files/{attachment.Id}"
                 })
        {
            using var response = await outsider.GetAsync(url);
            Assert.True(HttpStatusCode.NotFound == response.StatusCode, $"{url}: {response.StatusCode}");
        }

        var boards = await outsider.GetFromJsonAsync<List<BoardResponse>>("/api/boards");
        Assert.DoesNotContain(boards!, b => b.Id == board.Id);

        using var patch = await outsider.PatchAsJsonAsync($"/api/tasks/{task.Id}", new UpdateTaskRequest("Взлом", null, null));
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);

        // Владелец (глобальный Owner) видит всё по-прежнему.
        using var own = await owner.GetAsync($"/files/{attachment.Id}");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
    }

    [Fact]
    public async Task Visibility_Endpoint_Should_Validate()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Видимость", "PPB"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var unknown = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/visibility", new SetVisibilityRequest((BoardVisibility)7));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        using var missing = await owner.PutAsJsonAsync($"/api/boards/{Guid.NewGuid()}/visibility", new SetVisibilityRequest(BoardVisibility.Private));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
