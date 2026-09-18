using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;

namespace Flow.Infrastructure.OpenCode;

/// <summary>HTTP-клиент к V2 API OpenCode.</summary>
internal sealed class OpenCodeClient(IHttpClientFactory clients, OpenCodeOptions options) : IFlowAgentClient
{
    public const string HttpClientName = "flow-opencode";

    public async Task<OpenCodeAnswer> AskAsync(
        string workspaceDirectory,
        string question,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspaceDirectory))
            throw new ArgumentException("Рабочий каталог OpenCode не задан.", nameof(workspaceDirectory));

        if (string.IsNullOrWhiteSpace(question))
            throw new ArgumentException("Вопрос к OpenCode не задан.", nameof(question));

        var client = clients.CreateClient(HttpClientName);
        var session = await CreateSessionAsync(client, workspaceDirectory, cancellationToken);

        using (var promptResponse = await client.PostAsJsonAsync(
                   $"api/session/{session.Id}/prompt",
                   new PromptRequest(question.Trim()),
                   cancellationToken))
        {
            await EnsureSuccessAsync(promptResponse, "принять вопрос", cancellationToken);
        }

        using (var waitResponse = await client.PostAsync($"api/session/{session.Id}/wait", null, cancellationToken))
        {
            await EnsureSuccessAsync(waitResponse, "завершить обработку вопроса", cancellationToken);
        }

        var messages = await client.GetFromJsonAsync<MessagesResponse>(
            $"api/session/{session.Id}/message?type=assistant&order=desc&limit=1",
            cancellationToken);

        var answer = (messages?.Data ?? [])
            .SelectMany(message => message.Content ?? [])
            .Where(part => part.Type == "text")
            .Select(part => part.Text)
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));

        if (answer is null)
            throw new InvalidOperationException($"OpenCode не вернул текстовый ответ для сессии {session.Id}.");

        return new OpenCodeAnswer(session.Id, answer);
    }

    private async Task<Session> CreateSessionAsync(HttpClient client, string workspaceDirectory, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "api/session",
            new CreateSessionRequest(
                "Вопрос по исходному коду",
                new ModelReference(options.ProviderId, options.ModelId),
                new LocationReference(workspaceDirectory)),
            cancellationToken);

        await EnsureSuccessAsync(response, "создать сессию", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken);
        return payload?.Data ?? throw new InvalidOperationException("OpenCode не вернул созданную сессию.");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string action,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = body.Length <= 500 ? body : body[..500] + "…";
        throw new HttpRequestException(
            $"OpenCode не смог {action}: {(int)response.StatusCode} {detail}",
            null,
            response.StatusCode);
    }

    private sealed record CreateSessionRequest(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("model")] ModelReference Model,
        [property: JsonPropertyName("location")] LocationReference Location);

    private sealed record ModelReference(
        [property: JsonPropertyName("providerID")] string ProviderId,
        [property: JsonPropertyName("id")] string Id);

    private sealed record LocationReference([property: JsonPropertyName("directory")] string Directory);

    private sealed record PromptRequest([property: JsonPropertyName("text")] string Text);

    private sealed record SessionResponse([property: JsonPropertyName("data")] Session? Data);

    private sealed record Session([property: JsonPropertyName("id")] string Id);

    private sealed record MessagesResponse([property: JsonPropertyName("data")] IReadOnlyList<AssistantMessage>? Data);

    private sealed record AssistantMessage(
        [property: JsonPropertyName("content")] IReadOnlyList<AssistantContent>? Content);

    private sealed record AssistantContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
