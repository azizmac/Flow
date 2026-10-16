using Flow.Domain.Entities.Agents;

namespace Flow.Application.Abstractions;

/// <summary>Провайдеры агента и общая строка настроек (<see cref="AgentSettings.SingletonId"/>).</summary>
public interface IAgentProviderRepository
{
    Task<IReadOnlyList<AgentProvider>> GetAllAsync(CancellationToken cancellationToken);

    Task<AgentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsByKeyAsync(string providerKey, CancellationToken cancellationToken);

    void Add(AgentProvider provider);

    void Remove(AgentProvider provider);

    /// <summary>Строка настроек; null — её ещё не создавали (модель по умолчанию — из конфигурации хоста).</summary>
    Task<AgentSettings?> GetSettingsAsync(CancellationToken cancellationToken);

    void AddSettings(AgentSettings settings);
}

/// <summary>
/// Шифрование ключей агента. Реализация — DataProtection хоста (purpose «Flow.Agent», ключи в DataProtection:KeysPath,
/// том auth-keys): без тех же ключей после восстановления базы <see cref="TryUnprotect"/> вернёт null, и провайдер
/// попросит ввести ключ заново, а не уронит запрос.
/// </summary>
public interface IAgentSecretProtector
{
    string Protect(string secret);

    string? TryUnprotect(string protectedSecret);
}

/// <summary>
/// Сервер агента (OpenCode) как цель конфигурации: состояние, каталог провайдеров, передача провайдеров и ключей,
/// пробный запрос. Ключи и кастомные провайдеры применяются без перезапуска контейнера; копию у себя OpenCode
/// держит только до пересоздания — дальше её снова передаёт синхронизация (<c>AgentSyncCommand</c>).
/// Ошибки связи — <see cref="HttpRequestException"/>/<see cref="TimeoutException"/>.
/// </summary>
public interface IAgentRuntime
{
    /// <summary>Модель из конфигурации хоста — запасная, пока в настройках Flow модель не выбрана.</summary>
    AgentModelRef ConfiguredModel { get; }

    /// <summary>Отвечает ли OpenCode и какие провайдеры он видит подключёнными. Не бросает: недоступность — в Error.</summary>
    Task<AgentRuntimeState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Каталог провайдеров OpenCode (кэшируется). Недоступен — бросает.</summary>
    Task<IReadOnlyList<AgentCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Привести OpenCode к желаемому состоянию: кастомные провайдеры — в его конфигурацию, ключи — в его хранилище
    /// учётных данных; провайдеры из <paramref name="removedKeys"/> и кастомные, которых во Flow больше нет, — выключить.
    /// Ошибка отдельного провайдера не останавливает остальных — она в результате по его ключу.
    /// </summary>
    Task<IReadOnlyDictionary<string, AgentApplyOutcome>> ApplyAsync(
        IReadOnlyList<AgentProviderConfig> providers,
        IReadOnlyCollection<string> removedKeys,
        CancellationToken cancellationToken);

    /// <summary>Пробный короткий запрос к модели: текст ответа; отказ провайдера — <see cref="HttpRequestException"/> с причиной.</summary>
    Task<string> CheckAsync(AgentModelRef model, CancellationToken cancellationToken);
}

public sealed record AgentModelRef(string ProviderKey, string ModelId);

/// <param name="Error">Почему провайдер не применён; null — применён.</param>
/// <param name="RestartRequired">Применён в конфигурацию, но сессии OpenCode увидят его только после перезапуска.</param>
public sealed record AgentApplyOutcome(string? Error, bool RestartRequired = false)
{
    public static readonly AgentApplyOutcome Ok = new(Error: null);
}

public sealed record AgentRuntimeState(bool Reachable, string? Version, string? Error, IReadOnlyCollection<string> Connected);

public sealed record AgentCatalogEntry(string Id, string Name, IReadOnlyList<string> Env, IReadOnlyList<(string Id, string Name)> Models);

/// <param name="ApiKey">Открытый ключ — существует только на время передачи в OpenCode; null — ключа нет.</param>
public sealed record AgentProviderConfig(
    string ProviderKey,
    string Name,
    bool IsCustom,
    string? BaseUrl,
    IReadOnlyList<(string Id, string Name)> Models,
    string? ApiKey)
{
    /// <summary>Ключ в логах и отладчике не показывается.</summary>
    public override string ToString() => $"{ProviderKey} (custom: {IsCustom}, key: {(ApiKey is null ? "no" : "yes")})";
}
