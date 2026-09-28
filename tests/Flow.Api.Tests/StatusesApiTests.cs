using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Статусы через HTTP (этап 3A): маршруты, коды ответов и разбор <c>?moveTo=</c>. Инварианты — в Flow.Domain.Tests,
/// перенос задач с журналом — в Flow.Application.Tests, SQL-порядок удаления — в Flow.Infrastructure.Tests.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class StatusesApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> CreateBoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    [Fact]
    public async Task Status_Routes_Should_Return_Board_Or_Error_Codes()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "STA");
        var initial = board.Statuses.Single(s => s.IsInitial).Id;
        var done = board.Statuses.Single(s => s.IsFinal).Id;

        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/statuses", new CreateStatusRequest("Отменена", StatusType.Done, IsFinal: true));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var afterCreate = (await created.Content.ReadFromJsonAsync<BoardResponse>())!;
        var cancelled = afterCreate.Statuses.Single(s => s.Name == "Отменена").Id;

        using var duplicate = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/statuses", new CreateStatusRequest("отменена"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var patched = await owner.PatchAsJsonAsync($"/api/boards/{board.Id}/statuses/{cancelled}", new UpdateStatusRequest(Name: "Отклонена", ClearType: true));
        Assert.Equal(HttpStatusCode.OK, patched.StatusCode);

        using var initialFinal = await owner.PatchAsJsonAsync($"/api/boards/{board.Id}/statuses/{initial}", new UpdateStatusRequest(IsFinal: true));
        Assert.Equal(HttpStatusCode.BadRequest, initialFinal.StatusCode);

        var order = afterCreate.Statuses.Select(s => s.Id).Reverse().ToList();
        using var reordered = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/statuses/order", new ReorderStatusesRequest(order));
        Assert.Equal(HttpStatusCode.OK, reordered.StatusCode);
        Assert.Equal(order, (await reordered.Content.ReadFromJsonAsync<BoardResponse>())!.Statuses.Select(s => s.Id));

        using var partial = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/statuses/order", new ReorderStatusesRequest(order[..2]));
        Assert.Equal(HttpStatusCode.BadRequest, partial.StatusCode);

        using var deleteInitial = await owner.DeleteAsync($"/api/boards/{board.Id}/statuses/{initial}?moveTo={done}");
        Assert.Equal(HttpStatusCode.BadRequest, deleteInitial.StatusCode);

        using var deleted = await owner.DeleteAsync($"/api/boards/{board.Id}/statuses/{cancelled}?moveTo={done}");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(4, (await deleted.Content.ReadFromJsonAsync<BoardResponse>())!.Statuses.Count);

        using var missing = await owner.PostAsJsonAsync($"/api/boards/{Guid.NewGuid()}/statuses", new CreateStatusRequest("X"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Developer_Should_Get_403()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "STB");
        using var userResponse = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("sta.dev", "sta.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));
        Assert.Equal(HttpStatusCode.Created, userResponse.StatusCode);
        var developer = (await userResponse.Content.ReadFromJsonAsync<UserResponse>())!;
        using var client = api.CreateClientAs(developer.Id);

        using var response = await client.PostAsJsonAsync($"/api/boards/{board.Id}/statuses", new CreateStatusRequest("Блок"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
