using System.Net;
using System.Net.Http.Json;
using System.Text;
using Flow.Application.Features.GitIntegration;
using Flow.Shared.Contracts.GitIntegration;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Интеграция с Git через HTTP (этап 5A): вебхук анонимен и живёт вне /api, коды приёма — 202, повтор 200,
/// неверная подпись 401, чужой репозиторий 404, большое тело 413; подключение и репозиторий — 201 и Admin-API.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GitWebhookApiTests(ApiFixture api)
{
    [Fact]
    public async Task Webhook_Is_Anonymous_Outside_Api_And_Checks_Signature()
    {
        using var owner = api.CreateClientAs();
        using var connectionResponse = await owner.PostAsJsonAsync("/api/git/connections", new CreateGitHostConnectionRequest(GitProvider.GitHub, "GitHub", "token"));
        var connection = (await connectionResponse.Content.ReadFromJsonAsync<GitHostConnectionResponse>())!;
        using var repositoryResponse = await owner.PostAsJsonAsync("/api/git/repositories", new AddGitRepositoryRequest(connection.Id, "101"));
        Assert.Equal(HttpStatusCode.Created, repositoryResponse.StatusCode);
        var repository = (await repositoryResponse.Content.ReadFromJsonAsync<GitRepositoryResponse>())!;
        var secret = api.Git.CreatedHooks[^1].Secret;

        using var anonymous = api.CreateClient();
        var body = Encoding.UTF8.GetBytes("""{"ref":"refs/heads/main","after":"x","commits":[]}""");
        HttpRequestMessage Hook(Guid repositoryId, string signatureSecret, string delivery)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"/hooks/git/{repositoryId}") { Content = new ByteArrayContent(body) };
            request.Headers.Add("X-GitHub-Event", "push");
            request.Headers.Add("X-GitHub-Delivery", delivery);
            request.Headers.Add("X-Hub-Signature-256", "sha256=" + GitSignatures.Sign(body, signatureSecret));
            return request;
        }

        using var accepted = await anonymous.SendAsync(Hook(repository.Id, secret, "h-1"));
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var duplicate = await anonymous.SendAsync(Hook(repository.Id, secret, "h-1"));
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var forged = await anonymous.SendAsync(Hook(repository.Id, "wrong", "h-2"));
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        using var unknown = await anonymous.SendAsync(Hook(Guid.NewGuid(), secret, "h-3"));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // Под /api вебхука нет: там только Bearer.
        using var underApi = await anonymous.PostAsync($"/api/hooks/git/{repository.Id}", new ByteArrayContent(body));
        Assert.NotEqual(HttpStatusCode.Accepted, underApi.StatusCode);

        using var huge = new HttpRequestMessage(HttpMethod.Post, $"/hooks/git/{repository.Id}") { Content = new ByteArrayContent(new byte[GitWebhookControllerLimit + 1]) };
        using var tooLarge = await anonymous.SendAsync(huge);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
    }

    private const int GitWebhookControllerLimit = 5 * 1024 * 1024;

    /// <summary>Этап 5B: дозагрузка истории — 202, неизвестный репозиторий — 404; повтор неизвестной доставки — 404.</summary>
    [Fact]
    public async Task Backfill_Is_Accepted_And_Unknown_Ids_Are_404()
    {
        using var owner = api.CreateClientAs();
        using var connectionResponse = await owner.PostAsJsonAsync("/api/git/connections", new CreateGitHostConnectionRequest(GitProvider.GitHub, "GitHub 5B", "token"));
        var connection = (await connectionResponse.Content.ReadFromJsonAsync<GitHostConnectionResponse>())!;
        api.Git.Remote.Add(new Flow.Application.Abstractions.GitRemoteRepository("205", "acme/backfill", "https://github.com/acme/backfill", "main"));
        using var repositoryResponse = await owner.PostAsJsonAsync("/api/git/repositories", new AddGitRepositoryRequest(connection.Id, "205"));
        var repository = (await repositoryResponse.Content.ReadFromJsonAsync<GitRepositoryResponse>())!;

        using var accepted = await owner.PostAsync($"/api/git/repositories/{repository.Id}/backfill", null);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var missing = await owner.PostAsync($"/api/git/repositories/{Guid.NewGuid()}/backfill", null);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var retry = await owner.PostAsync($"/api/git/deliveries/{Guid.NewGuid()}/retry", null);
        Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
    }

    /// <summary>Этап 5C: PUT привязки принимает и пустое тело (только привязать), и настройки автоматизации.</summary>
    [Fact]
    public async Task Binding_Accepts_An_Empty_Body_Or_Automation_Settings()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new Flow.Shared.Contracts.Boards.CreateBoardRequest("Автоматизация", "GITC"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<Flow.Shared.Contracts.Boards.BoardResponse>())!;
        using var connectionResponse = await owner.PostAsJsonAsync("/api/git/connections", new CreateGitHostConnectionRequest(GitProvider.GitHub, "GitHub 5C", "token"));
        var connection = (await connectionResponse.Content.ReadFromJsonAsync<GitHostConnectionResponse>())!;
        api.Git.Remote.Add(new Flow.Application.Abstractions.GitRemoteRepository("305", "acme/auto", "https://github.com/acme/auto", "main"));
        using var repositoryResponse = await owner.PostAsJsonAsync("/api/git/repositories", new AddGitRepositoryRequest(connection.Id, "305"));
        var repository = (await repositoryResponse.Content.ReadFromJsonAsync<GitRepositoryResponse>())!;

        using var plain = await owner.PutAsync($"/api/boards/{board.Id}/repositories/{repository.Id}", null);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        var done = board.Statuses.Single(s => s.IsFinal).Id;
        using var configured = await owner.PutAsJsonAsync($"/api/boards/{board.Id}/repositories/{repository.Id}", new UpdateGitBindingRequest(null, done, true));
        var list = (await configured.Content.ReadFromJsonAsync<List<GitBoardRepositoryResponse>>())!;
        Assert.Equal((done, true), (list.Single(r => r.RepositoryId == repository.Id).OnPullRequestMergedStatusId!.Value, list.Single(r => r.RepositoryId == repository.Id).SmartCommits));
    }

    /// <summary>Этап 5D: ветка и PR из карточки — 200 с «Разработкой», отказ хостинга и неверное имя — 400, чужая задача — 404.</summary>
    [Fact]
    public async Task Branch_And_Pull_Request_Routes_Answer_With_Development()
    {
        using var owner = api.CreateClientAs();
        using var boardResponse = await owner.PostAsJsonAsync("/api/boards", new Flow.Shared.Contracts.Boards.CreateBoardRequest("Действия", "GITX"));
        var board = (await boardResponse.Content.ReadFromJsonAsync<Flow.Shared.Contracts.Boards.BoardResponse>())!;
        using var taskResponse = await owner.PostAsJsonAsync($"/api/boards/{board.Id}/tasks", new Flow.Shared.Contracts.Tasks.CreateTaskRequest("Кнопка", null, null));
        var task = (await taskResponse.Content.ReadFromJsonAsync<Flow.Shared.Contracts.Tasks.TaskResponse>())!;
        using var connectionResponse = await owner.PostAsJsonAsync("/api/git/connections", new CreateGitHostConnectionRequest(GitProvider.GitHub, "GitHub 5D", "token"));
        var connection = (await connectionResponse.Content.ReadFromJsonAsync<GitHostConnectionResponse>())!;
        api.Git.Remote.Add(new Flow.Application.Abstractions.GitRemoteRepository("405", "acme/act", "https://github.com/acme/act", "main"));
        using var repositoryResponse = await owner.PostAsJsonAsync("/api/git/repositories", new AddGitRepositoryRequest(connection.Id, "405"));
        var repository = (await repositoryResponse.Content.ReadFromJsonAsync<GitRepositoryResponse>())!;
        using var bind = await owner.PutAsync($"/api/boards/{board.Id}/repositories/{repository.Id}", null);

        using var branch = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/development/branch", new CreateGitBranchRequest(repository.Id, "GITX-1-knopka"));
        Assert.Equal(HttpStatusCode.OK, branch.StatusCode);
        Assert.Equal("GITX-1-knopka", (await branch.Content.ReadFromJsonAsync<TaskDevelopmentResponse>())!.Branches.Single().ExternalId);
        using var pr = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/development/pull-request", new CreateGitPullRequestRequest(repository.Id, "GITX-1-knopka"));
        Assert.Equal(HttpStatusCode.OK, pr.StatusCode);
        Assert.Single((await pr.Content.ReadFromJsonAsync<TaskDevelopmentResponse>())!.PullRequests);

        using var bad = await owner.PostAsJsonAsync($"/api/tasks/{task.Id}/development/branch", new CreateGitBranchRequest(repository.Id, "плохое имя"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        using var missing = await owner.PostAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/development/branch", new CreateGitBranchRequest(repository.Id, "x-1"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }
}
