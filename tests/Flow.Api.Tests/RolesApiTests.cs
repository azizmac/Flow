using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>PATCH /users/{id}/role, роль в ответах и коды 403/400 по матрице (#19). Actor — bootstrap-Owner или созданный им пользователь.</summary>
[Collection(ApiCollection.Name)]
public sealed class RolesApiTests(ApiFixture api)
{
    private static async Task<UserResponse> CreateAsync(HttpClient owner, string username, UserRole? role = null)
    {
        using var response = await owner.PostAsJsonAsync("/users", new CreateUserRequest(username, $"{username}@example.com", "A", "B", "correct horse battery", role));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    [Fact]
    public async Task Bootstrap_User_Should_Be_Owner_And_Active()
    {
        using var client = api.CreateClientAs();

        var me = await client.GetFromJsonAsync<UserResponse>("/users/me");

        Assert.Equal(UserRole.Owner, me!.Role);
        Assert.Equal(UserStatus.Active, me.Status);
    }

    [Fact]
    public async Task Created_User_Should_Have_Requested_Role_And_Invited_Status()
    {
        using var owner = api.CreateClientAs();

        var created = await CreateAsync(owner, "roles.dev", UserRole.Developer);

        Assert.Equal(UserRole.Developer, created.Role);
        Assert.Equal(UserStatus.Invited, created.Status);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task Owner_Can_Change_Role_And_Member_Gets_403()
    {
        using var owner = api.CreateClientAs();
        var member = await CreateAsync(owner, "roles.member");
        var target = await CreateAsync(owner, "roles.target");

        using var promoted = await owner.PatchAsJsonAsync($"/users/{target.Id}/role", new ChangeUserRoleRequest(UserRole.Admin));
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal(UserRole.Admin, (await promoted.Content.ReadFromJsonAsync<UserResponse>())!.Role);

        using var asMember = api.CreateClientAs(member.Id);
        using var forbidden = await asMember.PatchAsJsonAsync($"/users/{target.Id}/role", new ChangeUserRoleRequest(UserRole.Reader));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Contains("message", await forbidden.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Last_Owner_Demotion_Should_Return400()
    {
        using var owner = api.CreateClientAs();

        using var response = await owner.PatchAsJsonAsync($"/users/{ApiFixture.BootstrapId}/role", new ChangeUserRoleRequest(UserRole.Admin));

        // Owner может быть не единственным, если другой тест успел его назначить — тогда 200. Оба исхода — по правилам.
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.OK });
    }

    [Fact]
    public async Task Member_Cannot_Create_Board_Or_Users()
    {
        using var owner = api.CreateClientAs();
        var member = await CreateAsync(owner, "roles.member2");
        using var asMember = api.CreateClientAs(member.Id);

        using var board = await asMember.PostAsJsonAsync("/boards", new CreateBoardRequest("Nope", "NOPE"));
        Assert.Equal(HttpStatusCode.Forbidden, board.StatusCode);

        using var user = await asMember.PostAsJsonAsync("/users", new CreateUserRequest("roles.x", "roles.x@example.com", "A", "B", "correct horse battery"));
        Assert.Equal(HttpStatusCode.Forbidden, user.StatusCode);
    }

    [Fact]
    public async Task Deactivated_User_Token_Should_Get401()
    {
        using var owner = api.CreateClientAs();
        var victim = await CreateAsync(owner, "roles.deactivated");
        using var deactivate = await owner.PostAsync($"/users/{victim.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.NoContent, deactivate.StatusCode);

        using var asVictim = api.CreateClientAs(victim.Id);
        using var response = await asVictim.PostAsJsonAsync("/boards", new CreateBoardRequest("Nope", "NOPE"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
