using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Шаблоны проектов и перенос конфигурации через HTTP (этап 3F): маршруты и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class BoardTemplateApiTests(ApiFixture api)
{
    private static readonly Guid KanbanId = new("0f10a000-0000-4000-8000-000000000002");

    [Fact]
    public async Task Template_And_Apply_Config_Routes()
    {
        using var owner = api.CreateClientAs();
        var templates = await owner.GetFromJsonAsync<List<BoardTemplateResponse>>("/api/board-templates");
        Assert.Contains(templates!, t => t.Id == KanbanId && t.IsBuiltIn);

        using var fromTemplate = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Поток", "BTA", KanbanId));
        Assert.Equal(HttpStatusCode.Created, fromTemplate.StatusCode);
        var source = (await fromTemplate.Content.ReadFromJsonAsync<BoardResponse>())!;
        Assert.Equal("Бэклог", source.Statuses[0].Name);
        using var unknown = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Нет", "BTX", Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);

        using var saved = await owner.PostAsJsonAsync($"/api/boards/{source.Id}/save-as-template", new SaveBoardTemplateRequest("Поток команды", null, false));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);
        var template = (await saved.Content.ReadFromJsonAsync<BoardTemplateResponse>())!;
        using var builtInDelete = await owner.DeleteAsync($"/api/board-templates/{KanbanId}");
        Assert.Equal(HttpStatusCode.BadRequest, builtInDelete.StatusCode);
        using var missingBoard = await owner.PostAsJsonAsync($"/api/boards/{Guid.NewGuid()}/save-as-template", new SaveBoardTemplateRequest("X", null, false));
        Assert.Equal(HttpStatusCode.NotFound, missingBoard.StatusCode);

        using var targetResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Цель", "BTB"));
        var target = (await targetResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        var request = new ApplyBoardConfigRequest([new ApplyConfigTarget(target.Id)], ConfigParts.Statuses | ConfigParts.Workflow);

        using var preview = await owner.PostAsJsonAsync($"/api/boards/{source.Id}/apply-config/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Contains("К работе", (await preview.Content.ReadFromJsonAsync<ApplyConfigPreviewResponse>())!.Targets.Single().StatusesCreated);
        using var applied = await owner.PostAsJsonAsync($"/api/boards/{source.Id}/apply-config", request);
        Assert.True((await applied.Content.ReadFromJsonAsync<ApplyBoardConfigResponse>())!.Results.Single().Changed);
        using var nothing = await owner.PostAsJsonAsync($"/api/boards/{source.Id}/apply-config", request with { Parts = ConfigParts.None });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        using var missingSource = await owner.PostAsJsonAsync($"/api/boards/{Guid.NewGuid()}/apply-config", request);
        Assert.Equal(HttpStatusCode.NotFound, missingSource.StatusCode);

        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("bta.dev", "bta.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));
        var developer = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;
        using var dev = api.CreateClientAs(developer.Id);
        using var forbidden = await dev.PostAsJsonAsync($"/api/boards/{source.Id}/apply-config", request);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var forbiddenDelete = await dev.DeleteAsync($"/api/board-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenDelete.StatusCode);

        using var deleted = await owner.DeleteAsync($"/api/board-templates/{template.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }
}
