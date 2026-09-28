using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>Группы через HTTP (этап 4C): маршруты и коды ответов.</summary>
[Collection(ApiCollection.Name)]
public sealed class GroupApiTests(ApiFixture api)
{
    [Fact]
    public async Task Group_Routes()
    {
        using var owner = api.CreateClientAs();
        using var created = await owner.PostAsJsonAsync("/api/groups", new SaveGroupRequest("API-группа"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var group = (await created.Content.ReadFromJsonAsync<GroupResponse>())!;
        using var duplicate = await owner.PostAsJsonAsync("/api/groups", new SaveGroupRequest("api-группа"));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("grp.api", "grp.api@example.com", "A", "B", "correct horse battery", UserRole.Member));
        var user = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;
        using var added = await owner.PutAsync($"/api/groups/{group.Id}/members/{user.Id}", null);
        Assert.Equal([user.Id], (await added.Content.ReadFromJsonAsync<GroupResponse>())!.MemberIds);
        using var missingUser = await owner.PutAsync($"/api/groups/{group.Id}/members/{Guid.NewGuid()}", null);
        Assert.Equal(HttpStatusCode.BadRequest, missingUser.StatusCode);

        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new CreateBoardRequest("Группы", "GAPI"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<BoardResponse>())!;
        using var linked = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/groups/{group.Id}", new SetBoardMemberRequest(ProjectRole.Developer));
        Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        Assert.Single((await linked.Content.ReadFromJsonAsync<BoardMembersResponse>())!.Groups!);

        using var member = api.CreateClientAs(user.Id);
        Assert.Contains(await member.GetFromJsonAsync<List<GroupResponse>>("/api/groups") ?? [], g => g.Id == group.Id);
        using var forbidden = await member.PostAsJsonAsync("/api/groups", new SaveGroupRequest("Моя"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var access = await member.GetFromJsonAsync<ProjectAccessResponse>($"/api/boards/{board.Id}/my-access");
        Assert.Equal(ProjectRole.Developer, access!.Role);

        using var unlinked = await owner.DeleteAsync($"/api/boards/{board.Id}/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.OK, unlinked.StatusCode);
        using var unlinkedAgain = await owner.DeleteAsync($"/api/boards/{board.Id}/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NotFound, unlinkedAgain.StatusCode);
        using var deleted = await owner.DeleteAsync($"/api/groups/{group.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var gone = await owner.PatchAsJsonAsync($"/api/groups/{group.Id}", new SaveGroupRequest("X"));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }
}
