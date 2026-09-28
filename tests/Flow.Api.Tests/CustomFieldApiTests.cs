using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.CustomFields;
using Flow.Shared.Contracts.Tasks;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Пользовательские поля через HTTP (этап 1D): маршруты, коды, значения в JSON ответа; правила — ниже по слоям.</summary>
[Collection(ApiCollection.Name)]
public sealed class CustomFieldApiTests(ApiFixture api)
{
    [Fact]
    public async Task Custom_Field_Routes_And_Codes()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Поля", "CFA"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;

        using var badKey = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/custom-fields", new CreateCustomFieldRequest("Bad Key", "Плохо", CustomFieldType.Text));
        Assert.Equal(HttpStatusCode.BadRequest, badKey.StatusCode);
        using var created = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/custom-fields", new CreateCustomFieldRequest("budget", "Бюджет", CustomFieldType.Number));
        var field = Assert.Single((await created.Content.ReadFromJsonAsync<BoardResponse>())!.CustomFields!);

        using var taskResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("T", null, null,
            CustomFields: new Dictionary<Guid, JsonElement?> { [field.Id] = JsonDocument.Parse("42").RootElement }));
        var task = (await taskResponse.Content.ReadFromJsonAsync<TaskResponse>())!;
        Assert.Equal(42m, task.CustomFields![field.Id].GetDecimal());

        using var wrong = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/custom-fields",
            new SetCustomFieldsRequest(new Dictionary<Guid, JsonElement?> { [field.Id] = JsonDocument.Parse("\"много\"").RootElement }));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var cleared = await owner.PatchAsJsonAsync($"/api/tasks/{task.Id}/custom-fields",
            new SetCustomFieldsRequest(new Dictionary<Guid, JsonElement?> { [field.Id] = null }));
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<TaskResponse>())!.CustomFields!);

        using var archived = await owner.PatchAsJsonAsync($"/api/boards/{board.Id}/custom-fields/{field.Id}", new UpdateCustomFieldRequest(IsArchived: true));
        Assert.True(Assert.Single((await archived.Content.ReadFromJsonAsync<BoardResponse>())!.CustomFields!).IsArchived);
        using var missing = await owner.PatchAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/custom-fields", new SetCustomFieldsRequest(new Dictionary<Guid, JsonElement?>()));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
