using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Доступ к проекту через HTTP (этап 4A): маршруты, коды ответов и то, что участие действительно меняет ответ
/// на изменяющий запрос. Таблица прав — в Flow.Application.Tests (ProjectAccessTests).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ProjectAccessApiTests(ApiFixture api)
{
    private static async Task<BoardResponse> CreateBoardAsync(HttpClient client, string key)
    {
        using var response = await client.PostAsJsonAsync("/api/boards", new CreateBoardRequest($"Board {key}", key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BoardResponse>())!;
    }

    private static async Task<UserResponse> CreateUserAsync(HttpClient owner, string username, UserRole role)
    {
        using var response = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest(username, $"{username}@example.com", "A", "B", "correct horse battery", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    [Fact]
    public async Task Reader_Becomes_Member_Of_One_Project()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PAA");
        var reader = await CreateUserAsync(owner, "pa.reader", UserRole.Reader);
        using var asReader = api.CreateClientAs(reader.Id);

        using var before = await asReader.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("До", null, null));
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        using var join = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/members/{reader.Id}", new SetBoardMemberRequest(ProjectRole.Member));
        Assert.Equal(HttpStatusCode.OK, join.StatusCode);
        Assert.Equal(reader.Id, Assert.Single((await join.Content.ReadFromJsonAsync<BoardMembersResponse>())!.Members).UserId);

        using var after = await asReader.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new CreateTaskRequest("После", null, null));
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);

        var mine = await asReader.GetFromJsonAsync<ProjectAccessResponse>($"/api/boards/{board.Id}/my-access");
        Assert.Equal(ProjectRole.Member, mine!.Role);
        Assert.Contains(ProjectPermission.CreateTask, mine.Permissions);

        var all = await asReader.GetFromJsonAsync<List<ProjectAccessResponse>>("/api/boards/my-access");
        Assert.Contains(all!, a => a.BoardId == board.Id && a.Role == ProjectRole.Member);
    }

    [Fact]
    public async Task Member_Endpoints_Should_Return_403_400_404()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PAB");
        var developer = await CreateUserAsync(owner, "pa.dev", UserRole.Developer);
        using var asDeveloper = api.CreateClientAs(developer.Id);

        using var forbidden = await asDeveloper.PutAsJsonAsync($"/api/boards/{board.Id}/members/{developer.Id}", new SetBoardMemberRequest(ProjectRole.Admin));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var unknownUser = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/members/{Guid.NewGuid()}", new SetBoardMemberRequest(ProjectRole.Member));
        Assert.Equal(HttpStatusCode.BadRequest, unknownUser.StatusCode);

        using var unknownRole = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/members/{developer.Id}", new SetBoardMemberRequest((ProjectRole)9));
        Assert.Equal(HttpStatusCode.BadRequest, unknownRole.StatusCode);

        using var notMember = await owner.DeleteAsync($"/api/boards/{board.Id}/members/{developer.Id}");
        Assert.Equal(HttpStatusCode.NotFound, notMember.StatusCode);

        using var noBoard = await owner.GetAsync($"/api/boards/{Guid.NewGuid()}/members");
        Assert.Equal(HttpStatusCode.NotFound, noBoard.StatusCode);

        using var noAccess = await owner.GetAsync($"/api/boards/{Guid.NewGuid()}/my-access");
        Assert.Equal(HttpStatusCode.NotFound, noAccess.StatusCode);
    }

    [Fact]
    public async Task DefaultRole_Endpoint_Should_Set_Clear_And_Reject_Admin()
    {
        using var owner = api.CreateClientAs();
        var board = await CreateBoardAsync(owner, "PAC");

        using var set = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/default-role", new SetDefaultRoleRequest(ProjectRole.Viewer));
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(ProjectRole.Viewer, (await set.Content.ReadFromJsonAsync<BoardMembersResponse>())!.DefaultRole);
        Assert.Equal(ProjectRole.Viewer, (await owner.GetFromJsonAsync<BoardResponse>($"/api/boards/{board.Id}"))!.DefaultRole);

        using var admin = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/default-role", new SetDefaultRoleRequest(ProjectRole.Admin));
        Assert.Equal(HttpStatusCode.BadRequest, admin.StatusCode);

        using var clear = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/default-role", new SetDefaultRoleRequest(null));
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode);
        Assert.Null((await clear.Content.ReadFromJsonAsync<BoardMembersResponse>())!.DefaultRole);

        using var missing = await owner.PutAsJsonAsync($"/api/boards/{Guid.NewGuid()}/default-role", new SetDefaultRoleRequest(null));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
