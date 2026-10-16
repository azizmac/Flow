using Flow.Application.Exceptions;
using Flow.Application.Features.Agents.Settings;
using Flow.Application.Tests.Fakes;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Agents;
using Xunit;

namespace Flow.Application.Tests.Features;

/// <summary>
/// Инфраструктура агента: провайдеры и ключи хранятся во Flow зашифрованными, наружу не отдаются, в OpenCode уходят
/// сразу после сохранения (без перезапуска) и заново — после пересоздания контейнера; права — глобальные Admin+.
/// </summary>
public class AgentSettingsFeatureTests
{
    private static readonly Guid Owner = TestMediatorFactory.OwnerId;
    private static readonly CancellationToken None = CancellationToken.None;

    private static Task<AgentProviderResponse> AddOpenRouter(AgentTestContext context, string? key = "sk-or-v1-0123456789abcd") =>
        context.Mediator.Send(new AgentProviderCreateCommand(Owner, "openrouter", false, null, null, null, key), None);

    [Fact]
    public async Task Catalog_Provider_Key_Is_Stored_Encrypted_And_Applied_At_Once()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        var response = await AddOpenRouter(context);

        var stored = context.Providers.Items.Single();
        Assert.Equal("a:sk-or-v1-0123456789abcd", stored.ApiKeyProtected);
        Assert.Equal("OpenRouter", response.Name);
        Assert.True(response.HasKey);
        Assert.Equal("…abcd", response.KeyHint);
        Assert.True(response.Connected);
        Assert.Null(response.ApplyError);
        Assert.NotNull(response.AppliedAt);
        Assert.Equal("sk-or-v1-0123456789abcd", context.Runtime.Keys["openrouter"]);
    }

    [Fact]
    public async Task Responses_Never_Carry_The_Key()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await AddOpenRouter(context);

        var list = await context.Mediator.Send(new AgentProviderListQuery(Owner), None);

        var json = System.Text.Json.JsonSerializer.Serialize(list);
        Assert.DoesNotContain("0123456789", json);
    }

    [Fact]
    public async Task Unknown_Catalog_Provider_Is_Rejected_While_Catalog_Is_Available()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            context.Mediator.Send(new AgentProviderCreateCommand(Owner, "no-such", false, null, null, null, "k"), None));
    }

    [Fact]
    public async Task Custom_Provider_Cannot_Take_A_Catalog_Id()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        await Assert.ThrowsAsync<ArgumentException>(() => context.Mediator.Send(new AgentProviderCreateCommand(Owner, "anthropic", true, "Мой",
            "https://llm.example.com/v1", [new AgentModelInfo("m1", "M1")], null), None));
    }

    [Fact]
    public async Task Custom_Provider_Without_Key_Is_Configured_In_OpenCode()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        var response = await context.Mediator.Send(new AgentProviderCreateCommand(Owner, "My-LLM", true, "Свой сервер",
            "http://host.docker.internal:1234/v1/", [new AgentModelInfo("qwen", "Qwen")], null), None);

        Assert.Equal("my-llm", response.ProviderKey);
        Assert.Equal("http://host.docker.internal:1234/v1", response.BaseUrl);
        Assert.False(response.HasKey);
        Assert.Contains("my-llm", context.Runtime.Custom);
        // Свой список провайдеров OpenCode читает на старте: кастомный ждёт перезапуска, и об этом сказано явно.
        Assert.True(response.RestartRequired);
        Assert.False(response.Connected);
        Assert.Equal(["Свой сервер"], (await context.Mediator.Send(new AgentStatusQuery(Owner), None)).RestartRequired);

        // Пока OpenCode не перезапущен, фоновая проверка не гоняет передачу по кругу.
        var applied = context.Runtime.ApplyCount;
        Assert.False(await context.Mediator.Send(new AgentSyncCommand(false), None));
        Assert.Equal(applied, context.Runtime.ApplyCount);

        // Перезапустили — синхронизация снимает отметку.
        context.Runtime.Restart();
        Assert.True(await context.Mediator.Send(new AgentSyncCommand(false), None));
        var provider = context.Providers.Items.Single();
        Assert.False(provider.RestartRequired);
        Assert.Empty((await context.Mediator.Send(new AgentStatusQuery(Owner), None)).RestartRequired);
    }

    [Fact]
    public async Task Catalog_Key_Needs_No_Restart()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        var response = await AddOpenRouter(context);

        Assert.False(response.RestartRequired);
        Assert.Empty((await context.Mediator.Send(new AgentStatusQuery(Owner), None)).RestartRequired);
    }

    [Fact]
    public async Task Duplicate_Provider_Is_Rejected()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await AddOpenRouter(context);

        await Assert.ThrowsAsync<InvalidOperationException>(() => AddOpenRouter(context));
    }

    [Fact]
    public async Task Unreachable_OpenCode_Keeps_Settings_And_Reports_Why()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        context.Runtime.Reachable = false;

        var response = await AddOpenRouter(context);

        Assert.Single(context.Providers.Items);
        Assert.Equal(AgentApplyErrors.Unreachable, response.ApplyError);
        Assert.False(response.Connected);
    }

    [Fact]
    public async Task Sync_Restores_Keys_After_OpenCode_Is_Recreated()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await AddOpenRouter(context);
        var applied = context.Runtime.ApplyCount;

        // Всё на месте — фоновая проверка ничего не передаёт.
        Assert.False(await context.Mediator.Send(new AgentSyncCommand(false), None));
        Assert.Equal(applied, context.Runtime.ApplyCount);

        context.Runtime.Recreate();
        Assert.True(await context.Mediator.Send(new AgentSyncCommand(false), None));
        Assert.Equal("sk-or-v1-0123456789abcd", context.Runtime.Keys["openrouter"]);
    }

    [Fact]
    public async Task Sync_Retries_After_OpenCode_Comes_Back()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        context.Runtime.Reachable = false;
        await AddOpenRouter(context);

        Assert.False(await context.Mediator.Send(new AgentSyncCommand(false), None));

        context.Runtime.Reachable = true;
        Assert.True(await context.Mediator.Send(new AgentSyncCommand(false), None));
        Assert.Null(context.Providers.Items.Single().ApplyError);
        Assert.True(context.Runtime.Keys.ContainsKey("openrouter"));
    }

    [Fact]
    public async Task Replacing_And_Clearing_The_Key_Reaches_OpenCode()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        var provider = await AddOpenRouter(context);

        var replaced = await context.Mediator.Send(new AgentProviderUpdateCommand(Owner, provider.Id, null, null, null, "sk-new-key-000000wxyz", false), None);
        Assert.Equal("…wxyz", replaced!.KeyHint);
        Assert.Equal("sk-new-key-000000wxyz", context.Runtime.Keys["openrouter"]);

        var cleared = await context.Mediator.Send(new AgentProviderUpdateCommand(Owner, provider.Id, null, null, null, null, true), None);
        Assert.False(cleared!.HasKey);
        Assert.False(context.Runtime.Keys.ContainsKey("openrouter"));
    }

    [Fact]
    public async Task Catalog_Provider_Address_Cannot_Be_Changed()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        var provider = await AddOpenRouter(context);

        await Assert.ThrowsAsync<ArgumentException>(() => context.Mediator.Send(
            new AgentProviderUpdateCommand(Owner, provider.Id, null, "https://evil.example.com", null, null, false), None));
    }

    [Fact]
    public async Task Key_With_Spaces_Is_Rejected()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        await Assert.ThrowsAsync<ArgumentException>(() => AddOpenRouter(context, "sk-or v1"));
    }

    [Fact]
    public async Task Delete_Removes_The_Key_From_OpenCode()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        var provider = await AddOpenRouter(context);

        Assert.True(await context.Mediator.Send(new AgentProviderDeleteCommand(Owner, provider.Id), None));

        Assert.Empty(context.Providers.Items);
        Assert.False(context.Runtime.Keys.ContainsKey("openrouter"));
        Assert.Contains("openrouter", context.Runtime.Disabled);
    }

    [Fact]
    public async Task Deleting_The_Default_Model_Provider_Falls_Back_To_Host_Model()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        var provider = await AddOpenRouter(context);
        await context.Mediator.Send(new AgentDefaultModelSetCommand(Owner, "openrouter", "openai/gpt-4o-mini"), None);

        await context.Mediator.Send(new AgentProviderDeleteCommand(Owner, provider.Id), None);

        var status = await context.Mediator.Send(new AgentStatusQuery(Owner), None);
        Assert.Equal("lmstudio", status.DefaultProvider);
        Assert.False(status.DefaultFromSettings);
    }

    [Fact]
    public async Task Undecryptable_Key_Asks_To_Enter_It_Again()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await AddOpenRouter(context);
        context.Providers.Items.Single().SetKey("broken", "…abcd", Owner, DateTime.UtcNow);

        var list = await context.Mediator.Send(new AgentApplyCommand(Owner), None);

        Assert.True(list.Single().NeedsKey);
        Assert.Equal(AgentApplyErrors.UndecryptableKey, list.Single().ApplyError);
    }

    [Fact]
    public async Task Default_Model_Falls_Back_To_Host_Configuration()
    {
        var context = TestMediatorFactory.CreateAgentContext();

        var status = await context.Mediator.Send(new AgentStatusQuery(Owner), None);

        Assert.Equal("lmstudio", status.DefaultProvider);
        Assert.False(status.DefaultFromSettings);
    }

    [Fact]
    public async Task Default_Model_Is_Set_And_Reset()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await AddOpenRouter(context);

        var set = await context.Mediator.Send(new AgentDefaultModelSetCommand(Owner, "openrouter", "openai/gpt-4o-mini"), None);
        Assert.Equal("openrouter", set.DefaultProvider);
        Assert.Equal("openai/gpt-4o-mini", set.DefaultModel);
        Assert.True(set.DefaultFromSettings);
        Assert.True(set.Configured);

        var reset = await context.Mediator.Send(new AgentDefaultModelSetCommand(Owner, null, null), None);
        Assert.Equal("lmstudio", reset.DefaultProvider);
        Assert.False(reset.DefaultFromSettings);
    }

    [Fact]
    public async Task Default_Model_Of_Custom_Provider_Must_Be_Listed()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        await context.Mediator.Send(new AgentProviderCreateCommand(Owner, "my-llm", true, "Свой", "https://llm.example.com/v1",
            [new AgentModelInfo("qwen", "Qwen")], null), None);

        await Assert.ThrowsAsync<ArgumentException>(() => context.Mediator.Send(new AgentDefaultModelSetCommand(Owner, "my-llm", "llama"), None));
    }

    [Fact]
    public async Task Check_Uses_Default_Model_And_Reports_Failure_As_Result()
    {
        var context = TestMediatorFactory.CreateAgentContext();
        context.Runtime.CheckFailure = "Провайдер отказал: invalid api key";

        var result = await context.Mediator.Send(new AgentCheckCommand(Owner, null, null), None);

        Assert.False(result.Ok);
        Assert.Contains("invalid api key", result.Message);
        Assert.Equal(new Flow.Application.Abstractions.AgentModelRef("lmstudio", "qwen3.6-35b-a3b"), context.Runtime.LastCheck);
    }

    [Theory]
    [InlineData(UserRole.Developer, false)]
    [InlineData(UserRole.Admin, true)]
    [InlineData(UserRole.Owner, true)]
    public async Task Agent_Is_Configured_By_Global_Admins(UserRole role, bool allowed)
    {
        var context = TestMediatorFactory.CreateAgentContext();
        var user = User.Create("actor", "actor@example.com", "Имя", "Фамилия");
        user.ChangeRole(role);
        user.MarkActive();
        context.Users.Add(user);

        Task List() => context.Mediator.Send(new AgentProviderListQuery(user.Id), None);
        Task Create() => context.Mediator.Send(new AgentProviderCreateCommand(user.Id, "openrouter", false, null, null, null, "sk-0123456789abcd"), None);

        if (allowed)
        {
            await List();
            await Create();
        }
        else
        {
            await Assert.ThrowsAsync<ForbiddenException>(List);
            await Assert.ThrowsAsync<ForbiddenException>(Create);
            Assert.Empty(context.Providers.Items);
        }
    }
}
