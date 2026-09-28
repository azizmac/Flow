using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Экраны через HTTP (этап 3C): разбор {typeId|default}/{context} в адресе и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class ScreenApiTests(ApiFixture api)
{
    [Fact]
    public async Task Screen_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Экраны", "SCA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        var bug = board.TaskTypes.Single(t => t.Kind == TaskTypeKind.Bug).Id;

        using var set = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/screens/{bug}/detail", new SetScreenRequest([new ScreenFieldDto("system:due")]));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Single(await owner.GetFromJsonAsync<List<TaskScreenResponse>>($"/api/boards/{board.Id}/screens") ?? []);

        using var badContext = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/screens/default/list", new SetScreenRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, badContext.StatusCode);
        using var badType = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/screens/bug/create", new SetScreenRequest([]));
        Assert.Equal(HttpStatusCode.BadRequest, badType.StatusCode);
        using var dueOnCreate = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/screens/default/create", new SetScreenRequest([new ScreenFieldDto("system:due")]));
        Assert.Equal(HttpStatusCode.BadRequest, dueOnCreate.StatusCode);

        using var reset = await owner.DeleteAsync($"/api/boards/{board.Id}/screens/{bug}/detail");
        Assert.Empty((await reset.Content.ReadFromJsonAsync<BoardResponse>())!.Screens!);
    }
}
