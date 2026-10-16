namespace Flow.Infrastructure.OpenCode;

/// <summary>Подключение Flow к HTTP API OpenCode.</summary>
public sealed class OpenCodeOptions
{
    public const string SectionName = "OpenCode";

    public string BaseUrl { get; set; } = "http://localhost:4096";

    public string Username { get; set; } = "opencode";

    public string Password { get; set; } = "flow-opencode-local";

    public string ProviderId { get; set; } = "lmstudio";

    public string ModelId { get; set; } = "qwen3.6-35b-a3b";

    public int TimeoutSeconds { get; set; } = 1800;

    /// <summary>
    /// Передавать провайдеров и ключи из «Настройки → Агент» в OpenCode: на старте и затем раз в
    /// <see cref="SyncIntervalSeconds"/>, если OpenCode пересоздан и ключей у него нет.
    /// </summary>
    public bool SyncEnabled { get; set; } = true;

    public int SyncIntervalSeconds { get; set; } = 30;

    /// <summary>Каталог внутри контейнера OpenCode для пробного запроса «Проверить» — любой существующий, файлы не читаются.</summary>
    public string CheckDirectory { get; set; } = "/workspaces";
}
