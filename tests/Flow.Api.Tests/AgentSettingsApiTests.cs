using System.Net;
using System.Net.Http.Json;
using Flow.Shared.Contracts.Agents;
using Flow.Shared.Contracts.Users;
using Xunit;

namespace Flow.Api.Tests;

/// <summary>
/// Провайдеры и ключи агента через HTTP: коды ответов, права Admin+ и то, что ключ не уходит обратно ни в одном ответе.
/// OpenCode в тестах не поднят — передача в него завершается ApplyError, а не ошибкой запроса.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AgentSettingsApiTests(ApiFixture api)
{
    private const string Key = "sk-api-test-0123456789abcd";

    [Fact]
    public async Task Agent_Provider_Routes_Hide_The_Key()
    {
        using var owner = api.CreateClientAs();

        using var created = await owner.PostAsJsonAsync("/api/agents/providers", new CreateAgentProviderRequest(
            "api-llm", true, "API LLM", "https://llm.example.com/v1", [new AgentModelInfo("qwen", "Qwen")], Key));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadAsStringAsync();
        Assert.DoesNotContain("0123456789", body);
        var provider = (await created.Content.ReadFromJsonAsync<AgentProviderResponse>())!;
        Assert.True(provider.HasKey);
        Assert.NotNull(provider.ApplyError);

        var list = await owner.GetStringAsync("/api/agents/providers");
        Assert.DoesNotContain("0123456789", list);

        using var duplicate = await owner.PostAsJsonAsync("/api/agents/providers", new CreateAgentProviderRequest(
            "api-llm", true, "Again", "https://llm.example.com/v1", [new AgentModelInfo("qwen", "Qwen")]));
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

        var status = await owner.GetFromJsonAsync<AgentStatusResponse>("/api/agents/status");
        Assert.False(status!.Reachable);

        using var model = await owner.PutAsJsonAsync("/api/agents/default-model", new SetAgentDefaultModelRequest("api-llm", "llama"));
        Assert.Equal(HttpStatusCode.BadRequest, model.StatusCode);

        using var deleted = await owner.DeleteAsync($"/api/agents/providers/{provider.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await owner.DeleteAsync($"/api/agents/providers/{provider.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Agent_Settings_Are_Forbidden_Below_Admin()
    {
        using var owner = api.CreateClientAs();
        using var createUser = await owner.PostAsJsonAsync("/api/users",
            new CreateUserRequest("agent.dev", "agent.dev@example.com", "A", "B", "correct horse battery", UserRole.Developer));
        var developer = (await createUser.Content.ReadFromJsonAsync<UserResponse>())!;

        using var client = api.CreateClientAs(developer.Id);
        using var list = await client.GetAsync("/api/agents/providers");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/agents/providers", new CreateAgentProviderRequest("openrouter", false, ApiKey: Key));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }
}
