using Flow.Application.Abstractions;
using Flow.Domain.Entities.Agents;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeAgentProviderRepository : IAgentProviderRepository
{
    public List<AgentProvider> Items { get; } = [];

    public AgentSettings? Settings { get; private set; }

    public Task<IReadOnlyList<AgentProvider>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AgentProvider>>(Items.OrderBy(p => p.ProviderKey).ToList());

    public Task<AgentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<bool> ExistsByKeyAsync(string providerKey, CancellationToken cancellationToken) =>
        Task.FromResult(Items.Any(p => p.ProviderKey == providerKey));

    public void Add(AgentProvider provider) => Items.Add(provider);

    public void Remove(AgentProvider provider) => Items.Remove(provider);

    public Task<AgentSettings?> GetSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public void AddSettings(AgentSettings settings) => Settings = settings;
}

/// <summary>Обратимое «шифрование» для тестов: префикс, а не криптография.</summary>
public sealed class FakeAgentSecretProtector : IAgentSecretProtector
{
    public string Protect(string secret) => "a:" + secret;

    public string? TryUnprotect(string protectedSecret) => protectedSecret.StartsWith("a:") ? protectedSecret[2..] : null;
}

/// <summary>OpenCode в памяти: что ему передали, какие провайдеры он «видит», доступен ли он вообще.</summary>
public sealed class FakeAgentRuntime : IAgentRuntime
{
    public bool Reachable { get; set; } = true;

    /// <summary>Ключи, которые сейчас лежат в «OpenCode».</summary>
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);

    /// <summary>Кастомные провайдеры в его конфигурации.</summary>
    public HashSet<string> Custom { get; } = new(StringComparer.Ordinal);

    /// <summary>Кастомные провайдеры, которые видят сессии: конфигурацию OpenCode читает только на старте.</summary>
    public HashSet<string> Loaded { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Disabled { get; } = new(StringComparer.Ordinal);

    public int ApplyCount { get; private set; }

    public string? CheckFailure { get; set; }

    public AgentModelRef? LastCheck { get; private set; }

    public List<AgentCatalogEntry> Catalog { get; } =
    [
        new("anthropic", "Anthropic", ["ANTHROPIC_API_KEY"], [("claude-sonnet-5-5", "Claude Sonnet 5.5")]),
        new("openrouter", "OpenRouter", ["OPENROUTER_API_KEY"], [("openai/gpt-4o-mini", "GPT-4o mini")])
    ];

    public AgentModelRef ConfiguredModel { get; } = new("lmstudio", "qwen3.6-35b-a3b");

    /// <summary>Контейнер пересоздан без тома: ключи и конфигурация пропали.</summary>
    public void Recreate()
    {
        Keys.Clear();
        Custom.Clear();
        Loaded.Clear();
        Disabled.Clear();
    }

    /// <summary>Процесс перезапущен: конфигурация прочитана заново.</summary>
    public void Restart()
    {
        Loaded.Clear();
        Loaded.UnionWith(Custom);
    }

    public Task<AgentRuntimeState> GetStateAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Reachable
            ? new AgentRuntimeState(true, "2.0.0", null, Keys.Keys.Concat(Loaded).Where(k => !Disabled.Contains(k)).Append("lmstudio").ToHashSet())
            : new AgentRuntimeState(false, null, "connection refused", new HashSet<string>()));

    public Task<IReadOnlyList<AgentCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken) =>
        Reachable ? Task.FromResult<IReadOnlyList<AgentCatalogEntry>>(Catalog) : Task.FromException<IReadOnlyList<AgentCatalogEntry>>(new HttpRequestException("connection refused"));

    public Task<IReadOnlyDictionary<string, AgentApplyOutcome>> ApplyAsync(IReadOnlyList<AgentProviderConfig> providers, IReadOnlyCollection<string> removedKeys,
        CancellationToken cancellationToken)
    {
        if (!Reachable)
            return Task.FromException<IReadOnlyDictionary<string, AgentApplyOutcome>>(new HttpRequestException("connection refused"));

        ApplyCount++;
        foreach (var key in removedKeys)
        {
            Keys.Remove(key);
            Disabled.Add(key);
        }

        foreach (var p in providers)
        {
            Disabled.Remove(p.ProviderKey);
            if (p.IsCustom)
                Custom.Add(p.ProviderKey);
            if (p.ApiKey is { } key)
                Keys[p.ProviderKey] = key;
            else
                Keys.Remove(p.ProviderKey);
        }

        foreach (var stale in Custom.Where(k => providers.All(p => p.ProviderKey != k)).ToList())
            Disabled.Add(stale);

        return Task.FromResult<IReadOnlyDictionary<string, AgentApplyOutcome>>(providers.ToDictionary(p => p.ProviderKey,
            p => new AgentApplyOutcome(null, p.IsCustom && !Loaded.Contains(p.ProviderKey))));
    }

    public Task<string> CheckAsync(AgentModelRef model, CancellationToken cancellationToken)
    {
        LastCheck = model;
        return CheckFailure is { } failure ? Task.FromException<string>(new HttpRequestException(failure)) : Task.FromResult("ok");
    }
}
