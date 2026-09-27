using System.Net;
using System.Text;
using Flow.Domain.Entities;
using Flow.Infrastructure.Scm;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Клиенты хостингов на записанных ответах (HttpMessageHandler-заглушка): адреса API, заголовки токена, тело
/// вебхука, разбор списков и понятные ошибки. Живые хостинги в тестах не дёргаются.
/// </summary>
public class ScmProviderClientTests
{
    private sealed class Recorder(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GitHub_Uses_Bearer_And_Creates_A_Json_Webhook()
    {
        var recorder = new Recorder(r => r.RequestUri!.AbsolutePath switch
        {
            "/user" => Json("""{"login":"octocat"}"""),
            "/user/repos" => Json("""[{"id":101,"full_name":"acme/web","html_url":"https://github.com/acme/web","default_branch":"main"},{"id":102,"full_name":"acme/api","html_url":"https://github.com/acme/api","default_branch":"dev"}]"""),
            _ => Json("""{"id":555}""", HttpStatusCode.Created)
        });
        var client = new ScmProviderClient(new Factory(recorder));
        var connection = ScmConnection.Create(ScmProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());
        var repository = ScmRepository.Create(connection.Id, "101", "acme/web", "https://github.com/acme/web", "main", "p");

        Assert.Equal("octocat", await client.CheckAsync(connection, "tok", CancellationToken.None));
        Assert.Equal(["acme/api"], (await client.ListRepositoriesAsync(connection, "tok", "api", CancellationToken.None)).Select(r => r.FullName));
        Assert.Equal("555", await client.CreateWebhookAsync(connection, "tok", repository, "https://flow/hooks/scm/1", "sec", CancellationToken.None));

        var (hook, body) = recorder.Requests[^1];
        Assert.Equal("https://api.github.com/repos/acme/web/hooks", hook.RequestUri!.ToString());
        Assert.Equal("Bearer tok", hook.Headers.Authorization!.ToString());
        Assert.Contains("\"secret\":\"sec\"", body);
        Assert.Contains("\"pull_request\"", body);
    }

    [Fact]
    public async Task GitLab_And_Gitea_Use_Their_Api_Roots_And_Tokens()
    {
        var recorder = new Recorder(_ => Json("""{"id":7,"username":"dev","path_with_namespace":"g/p","web_url":"https://gl.example.com/g/p"}"""));
        var client = new ScmProviderClient(new Factory(recorder));

        var gitlab = ScmConnection.Create(ScmProvider.GitLab, "GitLab", "https://gl.example.com/", "p", Guid.NewGuid());
        Assert.Equal("dev", await client.CheckAsync(gitlab, "glpat", CancellationToken.None));
        Assert.Equal("https://gl.example.com/api/v4/user", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Equal("glpat", recorder.Requests[^1].Request.Headers.GetValues("PRIVATE-TOKEN").Single());

        var forgejo = ScmConnection.Create(ScmProvider.Forgejo, "Forgejo", "https://git.example.com", "p", Guid.NewGuid());
        await client.CheckAsync(forgejo, "fj", CancellationToken.None);
        Assert.Equal("https://git.example.com/api/v1/user", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Equal("token fj", recorder.Requests[^1].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Errors_Become_Readable_Reasons()
    {
        var client = new ScmProviderClient(new Factory(new Recorder(_ => Json("{}", HttpStatusCode.Unauthorized))));
        var connection = ScmConnection.Create(ScmProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());

        var error = await Assert.ThrowsAsync<Flow.Application.Abstractions.ScmProviderException>(() => client.CheckAsync(connection, "x", CancellationToken.None));
        Assert.Contains("Токен", error.Message);
    }
}
