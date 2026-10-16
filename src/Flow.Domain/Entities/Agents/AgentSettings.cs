namespace Flow.Domain.Entities.Agents;

/// <summary>
/// Общие настройки агента — одна строка на установку (<see cref="SingletonId"/>): модель, на которой Flow открывает
/// сессии OpenCode. Пусто — модель из конфигурации хоста (секция OpenCode, переменные OPENCODE_PROVIDER_ID/MODEL_ID).
/// </summary>
public sealed class AgentSettings
{
    public const int ModelIdMaxLength = AgentProviderModel.IdMaxLength;

    public static readonly Guid SingletonId = Guid.Parse("0a6e0000-0000-0000-0000-000000000001");

    public Guid Id { get; private set; }

    public string? ProviderKey { get; private set; }

    public string? ModelId { get; private set; }

    public Guid? UpdatedById { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    private AgentSettings()
    {
        // EF Core
    }

    public static AgentSettings Create() => new() { Id = SingletonId };

    /// <summary>Модель по умолчанию; оба null — вернуться к модели из конфигурации хоста.</summary>
    public void SetDefaultModel(string? providerKey, string? modelId, Guid actorId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(providerKey) != string.IsNullOrWhiteSpace(modelId))
            throw new ArgumentException("Модель по умолчанию задаётся парой «провайдер + модель».", nameof(modelId));

        if (string.IsNullOrWhiteSpace(providerKey))
        {
            ProviderKey = null;
            ModelId = null;
        }
        else
        {
            var model = modelId!.Trim();
            if (model.Length > ModelIdMaxLength || model.Any(char.IsWhiteSpace))
                throw new ArgumentException($"Id модели — до {ModelIdMaxLength} символов без пробелов.", nameof(modelId));
            ProviderKey = AgentProvider.NormalizeKey(providerKey);
            ModelId = model;
        }

        UpdatedById = actorId;
        UpdatedAt = utcNow;
    }
}
