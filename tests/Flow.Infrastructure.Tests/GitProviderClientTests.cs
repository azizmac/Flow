using System.Net;
using System.Text;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Infrastructure.Scm;
using Xunit;

namespace Flow.Infrastructure.Tests;

/// <summary>
/// Клиенты хостингов на записанных ответах (HttpMessageHandler-заглушка): адреса API, заголовки токена, тело
/// вебхука, разбор списков и понятные ошибки. Живые хостинги в тестах не дёргаются.
/// </summary>
public class GitProviderClientTests
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
        var connection = GitHostConnection.Create(GitProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());
        var repository = GitRepository.Create(connection.Id, "101", "acme/web", "https://github.com/acme/web", "main", "p");

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

        var gitlab = GitHostConnection.Create(GitProvider.GitLab, "GitLab", "https://gl.example.com/", "p", Guid.NewGuid());
        Assert.Equal("dev", await client.CheckAsync(gitlab, "glpat", CancellationToken.None));
        Assert.Equal("https://gl.example.com/api/v4/user", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Equal("glpat", recorder.Requests[^1].Request.Headers.GetValues("PRIVATE-TOKEN").Single());

        var forgejo = GitHostConnection.Create(GitProvider.Forgejo, "Forgejo", "https://git.example.com", "p", Guid.NewGuid());
        await client.CheckAsync(forgejo, "fj", CancellationToken.None);
        Assert.Equal("https://git.example.com/api/v1/user", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Equal("token fj", recorder.Requests[^1].Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Errors_Become_Readable_Reasons()
    {
        var client = new ScmProviderClient(new Factory(new Recorder(_ => Json("{}", HttpStatusCode.Unauthorized))));
        var connection = GitHostConnection.Create(GitProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());

        var error = await Assert.ThrowsAsync<Flow.Application.Abstractions.ScmProviderException>(() => client.CheckAsync(connection, "x", CancellationToken.None));
        Assert.Contains("Токен", error.Message);
    }

    [Fact]
    public async Task History_Pages_Until_Limit_And_Stops_At_Old_Commits()
    {
        var old = "2026-08-01T00:00:00Z";
        var recorder = new Recorder(r =>
        {
            var query = r.RequestUri!.Query;
            if (r.RequestUri.AbsolutePath.EndsWith("/pulls"))
                return Json("[" + string.Join(",", Enumerable.Range(1, 3).Select(n =>
                    """{"number":N,"title":"WEB-N","state":"open","html_url":"u","user":{"login":"o"},"head":{"ref":"h"},"base":{"ref":"main"}}""".Replace("N", n.ToString()))) + "]");
            // Первая страница коммитов полная (50 у Gitea), на второй — один свежий и один старше даты: на нём остановка.
            var fresh = """{"sha":"s","html_url":"u","commit":{"message":"m","author":{"email":"a@b.c","date":"2026-09-20T00:00:00Z"}}}""";
            return query.Contains("page=1&") || query.EndsWith("page=1")
                ? Json("[" + string.Join(",", Enumerable.Repeat(fresh, 50)) + "]")
                : Json("[" + fresh + "," + fresh.Replace("2026-09-20T00:00:00Z", old) + "]");
        });
        var client = new ScmProviderClient(new Factory(recorder));
        var connection = GitHostConnection.Create(GitProvider.Gitea, "Gitea", "https://git.example.com", "p", Guid.NewGuid());
        var repository = GitRepository.Create(connection.Id, "9", "acme/web", "https://git.example.com/acme/web", "dev", "p");

        var history = await client.GetHistoryAsync(connection, "tok", repository, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), 2, 500, CancellationToken.None);

        Assert.Equal(["1", "2"], history.PullRequests.Select(p => p.Number));
        Assert.Equal(51, history.Commits.Count);
        var commitCalls = recorder.Requests.Select(x => x.Request.RequestUri!.ToString()).Where(u => u.Contains("/commits")).ToList();
        Assert.Equal(2, commitCalls.Count);
        Assert.Contains("sha=dev&since=2026-09-01T00%3A00%3A00Z&limit=50&page=1", commitCalls[0]);
    }

    [Fact]
    public async Task Exhausted_Rate_Limit_Carries_The_Reset_Time()
    {
        var reset = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var client = new ScmProviderClient(new Factory(new Recorder(_ =>
        {
            var response = Json("""{"message":"API rate limit exceeded"}""", HttpStatusCode.Forbidden);
            response.Headers.Add("X-RateLimit-Remaining", "0");
            response.Headers.Add("X-RateLimit-Reset", reset.ToString());
            return response;
        })));
        var connection = GitHostConnection.Create(GitProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());
        var repository = GitRepository.Create(connection.Id, "1", "acme/web", "https://github.com/acme/web", "main", "p");

        var error = await Assert.ThrowsAsync<Flow.Application.Abstractions.ScmRateLimitException>(() =>
            client.GetHistoryAsync(connection, "tok", repository, DateTime.UtcNow.AddDays(-30), 100, 100, CancellationToken.None));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(reset).UtcDateTime, error.ResetAt);

        // Обычный 403 без нулевого остатка — это права, а не лимит.
        var plain = new ScmProviderClient(new Factory(new Recorder(_ => Json("{}", HttpStatusCode.Forbidden))));
        var forbidden = await Assert.ThrowsAsync<Flow.Application.Abstractions.ScmProviderException>(() => plain.CheckAsync(connection, "tok", CancellationToken.None));
        Assert.IsNotType<Flow.Application.Abstractions.ScmRateLimitException>(forbidden);
    }

    [Fact]
    public async Task GitHub_App_Exchanges_A_Signed_Jwt_For_A_Cached_Installation_Token()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var pem = rsa.ExportRSAPrivateKeyPem();
        var recorder = new Recorder(r => r.RequestUri!.AbsolutePath switch
        {
            "/app/installations/678/access_tokens" => Json($$"""{"token":"ghs_install","expires_at":"{{DateTime.UtcNow.AddHours(1):O}}"}""", HttpStatusCode.Created),
            "/app" => Json("""{"slug":"flow-tracker"}"""),
            _ => Json("""{"total_count":1,"repositories":[{"id":5,"full_name":"org/repo","html_url":"https://github.com/org/repo","default_branch":"main"}]}""")
        });
        var client = new ScmProviderClient(new Factory(recorder), new GitHubAppTokens());
        var connection = GitHostConnection.Create(GitProvider.GitHub, "App", null, "p", Guid.NewGuid(), GitAuthenticationKind.GitHubApp, 12345, 678);

        Assert.Equal("flow-tracker[bot]", await client.CheckAsync(connection, pem, CancellationToken.None));
        Assert.Equal(["org/repo"], (await client.ListRepositoriesAsync(connection, pem, null, CancellationToken.None)).Select(r => r.FullName));
        Assert.Equal(["org/repo"], (await client.ListRepositoriesAsync(connection, pem, null, CancellationToken.None)).Select(r => r.FullName));

        // Токен установки выписан один раз и дальше берётся из кэша.
        var paths = recorder.Requests.Select(x => x.Request.RequestUri!.AbsolutePath).ToList();
        Assert.Equal(1, paths.Count(p => p.EndsWith("access_tokens")));
        Assert.Equal("Bearer ghs_install", recorder.Requests.Last().Request.Headers.Authorization!.ToString());

        // JWT: RS256, iss — Id приложения, подпись проверяется открытым ключом.
        var jwt = recorder.Requests.First(x => x.Request.RequestUri!.AbsolutePath.EndsWith("access_tokens")).Request.Headers.Authorization!.Parameter!;
        var parts = jwt.Split('.');
        static byte[] Decode(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
        Assert.Contains("\"iss\":\"12345\"", Encoding.UTF8.GetString(Decode(parts[1])));
        Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), Decode(parts[2]),
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1));

        // Не PEM — понятная ошибка, а не исключение криптографии.
        var broken = GitHostConnection.Create(GitProvider.GitHub, "App", null, "p", Guid.NewGuid(), GitAuthenticationKind.GitHubApp, 1, 2);
        var error = await Assert.ThrowsAsync<Flow.Application.Abstractions.ScmProviderException>(() => client.CheckAsync(broken, "not a key", CancellationToken.None));
        Assert.Contains("PEM", error.Message);
    }

    /// <summary>
    /// Этап 5D: запись на хостинг. GitHub — ветка через sha головы и git/refs, PR с draft; GitLab — ветка
    /// query-параметрами, MR с «Draft:», заметка в MR; Gitea — branches и «WIP:», комментарий через issues.
    /// </summary>
    [Fact]
    public async Task Branches_Pull_Requests_And_Comments_Go_To_Provider_Endpoints()
    {
        var recorder = new Recorder(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/git/ref/heads/main") => Json("""{"object":{"sha":"abc123"}}"""),
            var p when p.EndsWith("/pulls") => Json("""{"number":12,"title":"WEB-1 x","state":"open","html_url":"https://github.com/acme/web/pull/12","user":{"login":"bot"},"head":{"ref":"WEB-1"},"base":{"ref":"main"}}""", HttpStatusCode.Created),
            var p when p.EndsWith("/merge_requests") => Json("""{"iid":3,"title":"Draft: WEB-1 x","state":"opened","draft":true,"web_url":"https://gl.example.com/g/p/-/merge_requests/3","source_branch":"WEB-1","target_branch":"main"}""", HttpStatusCode.Created),
            _ => Json("{}", HttpStatusCode.Created)
        });
        var client = new ScmProviderClient(new Factory(recorder));

        var github = GitHostConnection.Create(GitProvider.GitHub, "GitHub", null, "p", Guid.NewGuid());
        var ghRepo = GitRepository.Create(github.Id, "101", "acme/web", "https://github.com/acme/web", "main", "p");
        await client.CreateBranchAsync(github, "tok", ghRepo, "WEB-1", "main", CancellationToken.None);
        Assert.Equal("https://api.github.com/repos/acme/web/git/refs", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Contains("\"sha\":\"abc123\"", recorder.Requests[^1].Body);
        var pr = await client.CreatePullRequestAsync(github, "tok", ghRepo, "WEB-1", "main", "WEB-1 x", "body", true, CancellationToken.None);
        Assert.Equal(("12", GitDevelopmentLinkState.Open), (pr.Number, pr.State));
        Assert.Contains("\"draft\":true", recorder.Requests[^1].Body);
        await client.CommentOnPullRequestAsync(github, "tok", ghRepo, "12", "Задача", CancellationToken.None);
        Assert.Equal("https://api.github.com/repos/acme/web/issues/12/comments", recorder.Requests[^1].Request.RequestUri!.ToString());

        var gitlab = GitHostConnection.Create(GitProvider.GitLab, "GitLab", "https://gl.example.com", "p", Guid.NewGuid());
        var glRepo = GitRepository.Create(gitlab.Id, "7", "g/p", "https://gl.example.com/g/p", "main", "p");
        await client.CreateBranchAsync(gitlab, "tok", glRepo, "WEB-1", "main", CancellationToken.None);
        Assert.Equal("https://gl.example.com/api/v4/projects/7/repository/branches?branch=WEB-1&ref=main", recorder.Requests[^1].Request.RequestUri!.ToString());
        var mr = await client.CreatePullRequestAsync(gitlab, "tok", glRepo, "WEB-1", "main", "WEB-1 x", "body", true, CancellationToken.None);
        Assert.Equal(("3", GitDevelopmentLinkState.Draft), (mr.Number, mr.State));
        Assert.Contains("Draft: WEB-1 x", recorder.Requests[^1].Body);
        await client.CommentOnPullRequestAsync(gitlab, "tok", glRepo, "3", "Задача", CancellationToken.None);
        Assert.Equal("https://gl.example.com/api/v4/projects/7/merge_requests/3/notes", recorder.Requests[^1].Request.RequestUri!.ToString());

        var gitea = GitHostConnection.Create(GitProvider.Gitea, "Gitea", "https://git.example.com", "p", Guid.NewGuid());
        var gtRepo = GitRepository.Create(gitea.Id, "9", "acme/web", "https://git.example.com/acme/web", "main", "p");
        await client.CreateBranchAsync(gitea, "tok", gtRepo, "WEB-1", "main", CancellationToken.None);
        Assert.Equal("https://git.example.com/api/v1/repos/acme/web/branches", recorder.Requests[^1].Request.RequestUri!.ToString());
        Assert.Contains("\"old_branch_name\":\"main\"", recorder.Requests[^1].Body);
    }
}
