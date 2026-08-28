using System.Text.Json.Serialization;

namespace Flow.Ai.Client;

public sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("options")]
    public ChatOptions? Options { get; init; }
}

internal sealed class ChatOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }
}

internal sealed class ChatResponse
{
    [JsonPropertyName("message")]
    public required ChatMessage Message { get; init; }
}
