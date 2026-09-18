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

    public int TimeoutSeconds { get; set; } = 300;
}
