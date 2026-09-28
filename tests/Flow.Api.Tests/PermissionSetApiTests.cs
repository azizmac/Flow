using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Наборы прав через HTTP (этап 4E): маршруты и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class PermissionSetApiTests(ApiFixture api)
{
    [Fact]
    public async Task Permission_Set_Routes()
    {
        using var owner = api.CreateClientAs();
        var all = await owner.GetFromJsonAsync<List<PermissionSetResponse>>("/api/permission-sets");
        Assert.Equal(4, all!.Count(s => s.IsBuiltIn));

        using var created = await owner.PostAsJsonAsync("/api/permission-sets",
            new SavePermissionSetRequest("API-набор", null, ProjectRole.Member, [ProjectPermission.Comment]));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var set = (await created.Content.ReadFromJsonAsync<PermissionSetResponse>())!;
        using var builtIn = await owner.PatchAsJsonAsync($"/api/permission-sets/{all[0].Id}",
            new SavePermissionSetRequest("X", null, ProjectRole.Viewer, []));
        Assert.Equal(HttpStatusCode.BadRequest, builtIn.StatusCode);

        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Наборы", "PSAPI"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("pset.api", "pset.api@example.com", "A", "B", "correct horse battery", UserRole.Admin));
        var admin = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;
        using var member = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/members/{admin.Id}", new SetBoardMemberRequest(ProjectRole.Admin, set.Id));
        Assert.Equal(set.Id, (await member.Content.ReadFromJsonAsync<BoardMembersResponse>())!.Members.Single(m => m.UserId == admin.Id).PermissionSetId);

        using var adminClient = api.CreateClientAs(admin.Id);
        using var forbidden = await adminClient.PostAsJsonAsync("/api/permission-sets", new SavePermissionSetRequest("Мой", null, ProjectRole.Viewer, []));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var deleted = await owner.DeleteAsync($"/api/permission-sets/{set.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await owner.DeleteAsync($"/api/permission-sets/{set.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
