using System.Net;
using System.Net.Http.Json;
using Flow.Application.Exceptions;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthenticationTests(ApiFixture api)
{
    [Fact]
    public async Task Root_Should_Be_Anonymous()
    {
        using var client = api.CreateClient();

        using var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/users")]
    [InlineData("/boards")]
    [InlineData("/users/me")]
    public async Task Endpoints_WithoutToken_Should_Return401(string url)
    {
        using var client = api.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString());
    }

    [Fact]
    public async Task Token_With_Wrong_Audience_Or_Issuer_Should_Return401()
    {
        using var client = api.CreateClient();

        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFixture.CreateToken(ApiFixture.BootstrapId, audience: "flow-auth"));
        using var wrongAudience = await client.GetAsync("/users");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFixture.CreateToken(ApiFixture.BootstrapId, issuer: "http://evil.test"));
        using var wrongIssuer = await client.GetAsync("/users");
        Assert.Equal(HttpStatusCode.Unauthorized, wrongIssuer.StatusCode);
    }

    [Fact]
    public async Task Valid_Token_Should_Open_Api()
    {
        using var client = api.CreateClientAs();

        var users = await client.GetFromJsonAsync<List<UserResponse>>("/users");

        Assert.NotNull(users);
        Assert.Contains(users!, u => u.Id == ApiFixture.BootstrapId);
    }

    [Fact]
    public async Task Me_Should_Return_Profile_Of_Sub()
    {
        using var client = api.CreateClientAs();

        var me = await client.GetFromJsonAsync<UserResponse>("/users/me");

        Assert.Equal(ApiFixture.BootstrapId, me!.Id);
        Assert.Equal("admin", me.Username);
        Assert.Equal("admin@flow.com", me.Email);
        Assert.Equal("Admin Flow", me.FullName);
    }

    [Fact]
    public async Task Me_For_Unknown_Sub_Should_Return401()
    {
        using var client = api.CreateClientAs(Guid.NewGuid());

        using var response = await client.GetAsync("/users/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_Should_Require_Password_And_Create_Account_In_Auth()
    {
        using var client = api.CreateClientAs();

        using var noPassword = await client.PostAsJsonAsync("/users", new CreateUserRequest("nopw", "nopw@example.com", "A", "B"));
        Assert.Equal(HttpStatusCode.BadRequest, noPassword.StatusCode);

        api.Accounts.Calls.Clear();
        using var created = await client.PostAsJsonAsync("/users", new CreateUserRequest("withpw", "withpw@example.com", "A", "B", "correct horse battery"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        var call = Assert.Single(api.Accounts.CallsTo("Create"));
        Assert.Equal(user!.Id, call.Id);
        Assert.Equal("correct horse battery", call.Password);
    }

    [Fact]
    public async Task ChangePassword_Should_Call_Auth_And_Return204()
    {
        using var client = api.CreateClientAs();
        api.Accounts.Calls.Clear();

        using var response = await client.PostAsJsonAsync($"/users/{ApiFixture.BootstrapId}/password", new ChangePasswordRequest("admin", "new strong password"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var call = Assert.Single(api.Accounts.CallsTo("ChangePassword"));
        Assert.Equal("admin", call.CurrentPassword);
    }

    [Fact]
    public async Task Auth_Unavailable_Should_Return502()
    {
        using var client = api.CreateClientAs();
        using var created = await client.PostAsJsonAsync("/users", new CreateUserRequest("todisable", "todisable@example.com", "A", "B", "correct horse battery"));
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        api.Accounts.DisableEnableException = new AuthUnavailableException("down");

        try
        {
            using var response = await client.PostAsync($"/users/{user!.Id}/deactivate", null);

            Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
            var stillActive = await client.GetFromJsonAsync<UserResponse>($"/users/{user.Id}");
            Assert.True(stillActive!.IsActive);
        }
        finally
        {
            api.Accounts.DisableEnableException = null;
        }
    }
}
