namespace Flow.Shared.Contracts.Agents;

// Провайдеры моделей и ключи агента (OpenCode) — «Настройки → Агент», Admin+. Ключ API уходит на сервер и обратно
// не возвращается никогда: в ответах только HasKey и подсказка KeyHint («…a1b2»).

public sealed record AgentModelInfo(string Id, string Name);

/// <param name="ProviderKey">Id провайдера в OpenCode (`openrouter`, `my-llm`).</param>
/// <param name="NeedsKey">Ключ сохранён, но не расшифровывается (потеряны ключи DataProtection) — нужно ввести заново.</param>
/// <param name="AppliedAt">Когда конфигурация в последний раз ушла в OpenCode.</param>
/// <param name="ApplyError">Почему последняя передача в OpenCode не удалась.</param>
/// <param name="Connected">OpenCode сейчас видит провайдера подключённым.</param>
/// <param name="RestartRequired">Кастомный провайдер передан, но OpenCode увидит его только после перезапуска.</param>
public sealed record AgentProviderResponse(
    Guid Id,
    string ProviderKey,
    string Name,
    bool IsCustom,
    string? BaseUrl,
    IReadOnlyList<AgentModelInfo> Models,
    bool HasKey,
    string? KeyHint,
    DateTime? KeyUpdatedAt,
    Guid? KeyUpdatedById,
    bool NeedsKey,
    DateTime? AppliedAt,
    string? ApplyError,
    bool Connected,
    bool RestartRequired);

/// <summary>Провайдер из каталога — ProviderKey из <c>GET /agents/catalog</c>; кастомный — свой Id, адрес и модели.</summary>
public sealed record CreateAgentProviderRequest(
    string ProviderKey,
    bool IsCustom,
    string? Name = null,
    string? BaseUrl = null,
    IReadOnlyList<AgentModelInfo>? Models = null,
    string? ApiKey = null);

/// <summary>PATCH: null — не менять; ApiKey — новый ключ, ClearApiKey — удалить ключ.</summary>
public sealed record UpdateAgentProviderRequest(
    string? Name = null,
    string? BaseUrl = null,
    IReadOnlyList<AgentModelInfo>? Models = null,
    string? ApiKey = null,
    bool ClearApiKey = false);

/// <param name="Env">Переменные окружения, из которых OpenCode сам берёт ключ этого провайдера (справочно).</param>
public sealed record AgentCatalogProviderResponse(string Id, string Name, IReadOnlyList<string> Env, IReadOnlyList<AgentModelInfo> Models);

/// <param name="Reachable">OpenCode отвечает.</param>
/// <param name="ConnectedProviders">Провайдеры, которые OpenCode видит подключёнными.</param>
/// <param name="DefaultFromSettings">Модель задана в настройках Flow; false — взята из конфигурации хоста.</param>
/// <param name="Configured">Провайдер модели по умолчанию подключён — агенту есть чем отвечать.</param>
/// <param name="RestartRequired">Кастомные провайдеры, которые OpenCode увидит только после перезапуска; пусто — перезапуск не нужен.</param>
public sealed record AgentStatusResponse(
    bool Reachable,
    string? Version,
    string? Error,
    IReadOnlyList<string> ConnectedProviders,
    string DefaultProvider,
    string DefaultModel,
    bool DefaultFromSettings,
    bool Configured,
    IReadOnlyList<string> RestartRequired);

/// <summary>Оба null — вернуться к модели из конфигурации хоста.</summary>
public sealed record SetAgentDefaultModelRequest(string? ProviderKey, string? ModelId);

/// <summary>Без тела — проверить модель по умолчанию.</summary>
public sealed record AgentCheckRequest(string? ProviderKey = null, string? ModelId = null);

public sealed record AgentCheckResponse(bool Ok, string ProviderKey, string ModelId, string Message);
