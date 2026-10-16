using Flow.Application.Abstractions;

namespace Flow.Infrastructure.OpenCode;

/// <summary>Модель для новой сессии: выбранная в «Настройки → Агент», иначе — из секции OpenCode (ProviderId/ModelId).</summary>
internal sealed class OpenCodeModelSelector(IAgentProviderRepository agents, OpenCodeOptions options)
{
    public async Task<(string ProviderId, string ModelId)> GetAsync(CancellationToken cancellationToken) =>
        await agents.GetSettingsAsync(cancellationToken) is { ProviderKey: { } provider, ModelId: { } model }
            ? (provider, model)
            : (options.ProviderId, options.ModelId);
}
