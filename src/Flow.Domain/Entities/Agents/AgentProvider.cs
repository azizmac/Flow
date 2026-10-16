using System.Text.RegularExpressions;

namespace Flow.Domain.Entities.Agents;

/// <summary>Модель кастомного провайдера: Id — как его ждёт API провайдера, Name — подпись в интерфейсе.</summary>
public sealed class AgentProviderModel
{
    public const int IdMaxLength = 200;
    public const int NameMaxLength = 120;

    public string Id { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    private AgentProviderModel()
    {
        // EF Core (JSON)
    }

    internal AgentProviderModel(string id, string name)
    {
        Id = id;
        Name = name;
    }
}

/// <summary>
/// Провайдер моделей для агента (OpenCode): либо провайдер из каталога OpenCode (anthropic, openai, openrouter…), либо
/// кастомный OpenAI-совместимый — со своим адресом и списком моделей. Ключ API хранится только зашифрованным
/// (<see cref="ApiKeyProtected"/>), наружу уходит подсказка <see cref="KeyHint"/>. Источник истины — Flow: OpenCode получает
/// провайдеров и ключи от него и после пересоздания контейнера получает их заново.
/// </summary>
public sealed partial class AgentProvider
{
    public const int KeyMaxLength = 64;
    public const int NameMaxLength = 80;
    public const int UrlMaxLength = 500;
    public const int ErrorMaxLength = 500;
    public const int MaxModels = 50;

    private List<AgentProviderModel> _models = [];

    public Guid Id { get; private set; }

    /// <summary>Id провайдера в OpenCode (`openrouter`, `my-llm`): по нему OpenCode ищет ключ и модель — не меняется.</summary>
    public string ProviderKey { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Кастомный OpenAI-совместимый провайдер; иначе — провайдер из каталога OpenCode.</summary>
    public bool IsCustom { get; private set; }

    /// <summary>Адрес API кастомного провайдера (`https://llm.example.com/v1`); у провайдера из каталога — null.</summary>
    public string? BaseUrl { get; private set; }

    /// <summary>Модели кастомного провайдера; у провайдера из каталога список берётся из OpenCode.</summary>
    public IReadOnlyList<AgentProviderModel> Models => _models;

    /// <summary>Ключ API, зашифрованный DataProtection: в БД и в логах открытого текста нет. null — ключа нет.</summary>
    public string? ApiKeyProtected { get; private set; }

    /// <summary>Последние символы ключа для интерфейса (`…a1b2`).</summary>
    public string? KeyHint { get; private set; }

    public DateTime? KeyUpdatedAt { get; private set; }

    public Guid? KeyUpdatedById { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Когда конфигурация провайдера в последний раз ушла в OpenCode.</summary>
    public DateTime? AppliedAt { get; private set; }

    /// <summary>Почему последняя передача в OpenCode не удалась; null — удалась или ещё не было.</summary>
    public string? ApplyError { get; private set; }

    /// <summary>
    /// Конфигурация кастомного провайдера дошла до OpenCode, но сессии увидят её только после перезапуска процесса
    /// OpenCode: свой список провайдеров он читает на старте. Ключи провайдеров из каталога перезапуска не требуют.
    /// </summary>
    public bool RestartRequired { get; private set; }

    private AgentProvider()
    {
        // EF Core
    }

    public static AgentProvider CreateCatalog(string providerKey, string name, Guid createdById) =>
        Create(providerKey, name, false, null, [], createdById);

    public static AgentProvider CreateCustom(string providerKey, string name, string baseUrl,
        IEnumerable<(string Id, string Name)> models, Guid createdById) =>
        Create(providerKey, name, true, baseUrl, models, createdById);

    private static AgentProvider Create(string providerKey, string name, bool isCustom, string? baseUrl,
        IEnumerable<(string Id, string Name)> models, Guid createdById)
    {
        if (createdById == Guid.Empty)
            throw new ArgumentException("Author id must not be empty.", nameof(createdById));

        var provider = new AgentProvider
        {
            Id = Guid.NewGuid(),
            ProviderKey = NormalizeKey(providerKey),
            IsCustom = isCustom,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow
        };
        if (isCustom)
            provider.UpdateCustom(name, baseUrl, models);
        else
            provider.Rename(name);
        return provider;
    }

    public void Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new ArgumentException($"Название — от 1 до {NameMaxLength} символов.", nameof(name));
        Name = trimmed;
    }

    /// <summary>Название, адрес и модели кастомного провайдера — целиком.</summary>
    public void UpdateCustom(string name, string? baseUrl, IEnumerable<(string Id, string Name)> models)
    {
        if (!IsCustom)
            throw new InvalidOperationException("Адрес и модели задаются только у кастомного провайдера.");

        var url = NormalizeUrl(baseUrl) ?? throw new ArgumentException("У кастомного провайдера нужен адрес API.", nameof(baseUrl));
        var list = new List<AgentProviderModel>();
        foreach (var (id, modelName) in models)
        {
            var modelId = id?.Trim() ?? string.Empty;
            if (modelId.Length is 0 or > AgentProviderModel.IdMaxLength || modelId.Any(char.IsWhiteSpace))
                throw new ArgumentException($"Id модели — от 1 до {AgentProviderModel.IdMaxLength} символов без пробелов.", nameof(models));
            if (list.Any(m => string.Equals(m.Id, modelId, StringComparison.Ordinal)))
                throw new ArgumentException($"Модель «{modelId}» указана дважды.", nameof(models));
            var label = string.IsNullOrWhiteSpace(modelName) ? modelId : modelName.Trim();
            if (label.Length > AgentProviderModel.NameMaxLength)
                throw new ArgumentException($"Название модели — до {AgentProviderModel.NameMaxLength} символов.", nameof(models));
            list.Add(new AgentProviderModel(modelId, label));
        }

        if (list.Count == 0)
            throw new ArgumentException("У кастомного провайдера нужна хотя бы одна модель.", nameof(models));
        if (list.Count > MaxModels)
            throw new ArgumentException($"Не больше {MaxModels} моделей у провайдера.", nameof(models));

        Rename(name);
        BaseUrl = url;
        _models = list;
    }

    /// <summary>Новый ключ: зашифрованное значение и подсказка считаются снаружи, домен открытого ключа не видит.</summary>
    public void SetKey(string apiKeyProtected, string keyHint, Guid actorId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(apiKeyProtected))
            throw new ArgumentException("Ключ не может быть пустым.", nameof(apiKeyProtected));
        ApiKeyProtected = apiKeyProtected;
        KeyHint = keyHint;
        KeyUpdatedAt = utcNow;
        KeyUpdatedById = actorId;
    }

    public void ClearKey(Guid actorId, DateTime utcNow)
    {
        if (ApiKeyProtected is null)
            return;
        ApiKeyProtected = null;
        KeyHint = null;
        KeyUpdatedAt = utcNow;
        KeyUpdatedById = actorId;
    }

    /// <summary>Итог передачи в OpenCode: error = null — дошло; restartRequired — дошло, но ждёт перезапуска OpenCode.</summary>
    public void MarkApplied(string? error, bool restartRequired, DateTime utcNow)
    {
        if (error is null)
            AppliedAt = utcNow;
        ApplyError = error is null ? null : error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        RestartRequired = error is null && restartRequired;
    }

    /// <summary>Подсказка для интерфейса: последние четыре символа длинного ключа, у короткого — только маска.</summary>
    public static string HintOf(string apiKey)
    {
        var key = apiKey.Trim();
        return key.Length >= 12 ? "…" + key[^4..] : "••••";
    }

    public static string NormalizeKey(string providerKey)
    {
        var key = providerKey?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!KeyPattern().IsMatch(key))
            throw new ArgumentException("Id провайдера — латиница в нижнем регистре, цифры, «.», «_» и «-», до 64 символов.", nameof(providerKey));
        return key;
    }

    /// <summary>Адрес без хвостового «/»: только http(s) — иначе ключ ушёл бы по file:// или на чужую схему.</summary>
    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var value = url.Trim().TrimEnd('/');
        if (value.Length > UrlMaxLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Адрес API — http(s)-ссылка.", nameof(url));
        return value;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex KeyPattern();
}
