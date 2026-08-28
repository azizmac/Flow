namespace Flow.Ai.Client;

/// <summary>Тонкий клиент к локальному Ollama REST API (/api/chat).</summary>
public interface IOllamaClient
{
    Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
}
