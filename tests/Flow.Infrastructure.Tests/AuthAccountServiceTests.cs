using System.Net;
using System.Text;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>Юнит-тесты HTTP-клиента admin-API без Postgres и без Flow.Auth: ответы подменяет StubHandler.</summary>
public class AuthAccountServiceTests
{
    private static readonly Guid Id = Guid.NewGuid();

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object body) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage Token() => Json(HttpStatusCode.OK, new { access_token = "t0k3n", token_type = "Bearer", expires_in = 3600 });

    private static (AuthAccountService Service, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respondToAccounts)
    {
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith("/connect/token") ? Token() : respondToAccounts(request));

        var options = Options.Create(new AuthClientOptions
        {
            BaseUrl = "http://auth.test",
            ApiClient = { ClientId = "flow-api", Secret = "secret" }
        });
        var tokens = new ClientCredentialsTokenProvider(new StubHttpClientFactory(handler), options);
        var service = new AuthAccountService(new HttpClient(handler, disposeHandler: false), tokens, options);
        return (service, handler);
    }

    [Fact]
    public async Task Create_Should_Send_Bearer_And_Map_201_To_Success()
    {
        var (service, handler) = Create(_ => Json(HttpStatusCode.Created, new { id = Id }));

        var result = await service.CreateAsync(Id, "ilya", "ilya@example.com", "correct horse battery", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath == "/accounts");
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("t0k3n", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Conflict_Should_Map_To_EmailTaken_Or_UsernameTaken_By_Message()
    {
        var (byEmail, _) = Create(_ => Json(HttpStatusCode.Conflict, new { message = "Email 'x@example.com' is already taken." }));
        var (byName, _) = Create(_ => Json(HttpStatusCode.Conflict, new { message = "Username 'x' is already taken." }));

        Assert.Equal(AccountResultStatus.EmailTaken, (await byEmail.ChangeEmailAsync(Id, "x@example.com", CancellationToken.None)).Status);
        Assert.Equal(AccountResultStatus.UsernameTaken, (await byName.ChangeUsernameAsync(Id, "x", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task BadRequest_Should_Map_To_Invalid_With_Message()
    {
        var (service, _) = Create(_ => Json(HttpStatusCode.BadRequest, new { message = "Incorrect password." }));

        var result = await service.ChangePasswordAsync(Id, "wrong", "new strong password", CancellationToken.None);

        Assert.Equal(AccountResultStatus.Invalid, result.Status);
        Assert.Equal("Incorrect password.", result.Error);
    }

    [Fact]
    public async Task ServerError_Should_Throw_AuthUnavailable()
    {
        var (service, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<AuthUnavailableException>(() => service.DisableAsync(Id, CancellationToken.None));
    }

    [Fact]
    public async Task Network_Failure_Should_Throw_AuthUnavailable()
    {
        var (service, _) = Create(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<AuthUnavailableException>(() => service.EnableAsync(Id, CancellationToken.None));
    }

    [Fact]
    public async Task Token_Should_Be_Requested_Once_For_Several_Calls()
    {
        var (service, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await service.DisableAsync(Id, CancellationToken.None);
        await service.EnableAsync(Id, CancellationToken.None);
        await service.ChangeUsernameAsync(Id, "ilya", CancellationToken.None);

        Assert.Single(handler.Requests, r => r.RequestUri!.AbsolutePath.EndsWith("/connect/token"));
        Assert.Equal(3, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.StartsWith("/accounts")));
    }

    [Fact]
    public async Task Unauthorized_Should_Refresh_Token_And_Retry_Once()
    {
        var attempts = 0;
        var (service, handler) = Create(_ => ++attempts == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : new HttpResponseMessage(HttpStatusCode.NoContent));

        await service.DisableAsync(Id, CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal(2, handler.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/connect/token")));
    }
}
