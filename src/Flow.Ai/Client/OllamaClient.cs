using System.Net.Http.Json;
using Flow.Ai.Options;
using Microsoft.Extensions.Options;

namespace Flow.Ai.Client;

internal sealed class OllamaClient(HttpClient httpClient, IOptions<OllamaOptions> options) : IOllamaClient
{
    private readonly OllamaOptions _options = options.Value;

    public async Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default)
    {
        var request = new ChatRequest
        {
            Model = _options.GenerationModel,
            Messages = messages,
            Stream = false,
            Options = new ChatOptions { Temperature = _options.Temperature },
        };

        var response = await httpClient.PostAsJsonAsync("api/chat", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: cancellationToken)
                   ?? throw new InvalidOperationException("Ollama вернул пустой ответ.");

        return body.Message.Content;
    }
}
