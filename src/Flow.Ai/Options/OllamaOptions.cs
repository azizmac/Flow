namespace Flow.Ai.Options;

/// <summary>
/// Настройки локального Ollama-инференса. Секция конфигурации "Ollama"
/// (см. appsettings.json Flow.Api).
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434/";

    /// <summary>Модель для генерации текста/JSON/Mermaid. По умолчанию — верх диапазона 4-8B.</summary>
    public string GenerationModel { get; set; } = "qwen2.5:7b-instruct-q4_K_M";

    /// <summary>Модель для эмбеддингов базы знаний (RAG).</summary>
    public string EmbeddingModel { get; set; } = "bge-m3";

    public double Temperature { get; set; } = 0.4;

    /// <summary>Сколько раз Generator+Validator пробует получить валидный ответ, прежде чем сдаться.</summary>
    public int MaxAttempts { get; set; } = 3;
}
