using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Flow.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.OpenCode;

/// <summary>HTTP-клиент к V2 API OpenCode.</summary>
internal sealed class OpenCodeClient(IHttpClientFactory clients, OpenCodeOptions options, ILogger<OpenCodeClient> logger) : IFlowAgentClient
{
    public const string HttpClientName = "flow-opencode";

    public Task<OpenCodeAnswer> AskAsync(
        string workspaceDirectory,
        string question,
        CancellationToken cancellationToken) =>
        AskAsync(new FlowAgentRequest(workspaceDirectory, question), cancellationToken);

    public async Task<OpenCodeAnswer> AskAsync(FlowAgentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.WorkspaceDirectory))
            throw new ArgumentException("Рабочий каталог OpenCode не задан.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Question))
            throw new ArgumentException("Вопрос к OpenCode не задан.", nameof(request));

        var permissions = ReadOnlyPermissions(request);
        var client = clients.CreateClient(HttpClientName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds)));
        Session? session = null;

        try
        {
            session = await CreateSessionAsync(client, request.WorkspaceDirectory, permissions, timeout.Token);

            using (var promptResponse = await client.PostAsJsonAsync(
                       $"api/session/{session.Id}/prompt",
                       new PromptRequest(request.Question.Trim()),
                       timeout.Token))
            {
                await EnsureSuccessAsync(promptResponse, "принять вопрос", timeout.Token);
            }

            using (var waitResponse = await client.PostAsync($"api/session/{session.Id}/wait", null, timeout.Token))
            {
                await EnsureSuccessAsync(waitResponse, "завершить обработку вопроса", timeout.Token);
            }

            var messages = await client.GetFromJsonAsync<MessagesResponse>(
                $"api/session/{session.Id}/message?type=assistant&order=desc&limit=1",
                timeout.Token);

            var answer = string.Join("\n\n", (messages?.Data ?? [])
                .SelectMany(message => message.Content ?? [])
                .Where(part => part.Type == "text" && !string.IsNullOrWhiteSpace(part.Text))
                .Select(part => part.Text));

            if (string.IsNullOrWhiteSpace(answer))
                throw new InvalidOperationException($"OpenCode не вернул текстовый ответ для сессии {session.Id}.");

            return new OpenCodeAnswer(session.Id, answer);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (session is not null)
                await TryInterruptAsync(client, session.Id);
            throw;
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
                await TryInterruptAsync(client, session.Id);
            throw new TimeoutException("OpenCode не успел завершить анализ за отведённое время.");
        }
        catch (JsonException exception)
        {
            if (session is not null)
                await TryInterruptAsync(client, session.Id);
            logger.LogWarning(exception, "OpenCode returned invalid JSON for session {SessionId}", session?.Id);
            throw new HttpRequestException("OpenCode вернул некорректный ответ.", exception);
        }
        catch (HttpRequestException)
        {
            if (session is not null)
                await TryInterruptAsync(client, session.Id);
            throw;
        }
    }

    private async Task<Session> CreateSessionAsync(HttpClient client, string workspaceDirectory,
        IReadOnlyList<PermissionRule>? permissions, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(
            "api/session",
            new CreateSessionRequest(
                "Вопрос по исходному коду",
                new ModelReference(options.ProviderId, options.ModelId),
                new LocationReference(workspaceDirectory),
                permissions is null ? null : "plan",
                permissions),
            cancellationToken);

        await EnsureSuccessAsync(response, "создать сессию", cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<SessionResponse>(cancellationToken);
        return payload?.Data ?? throw new InvalidOperationException("OpenCode не вернул созданную сессию.");
    }

    private static IReadOnlyList<PermissionRule>? ReadOnlyPermissions(FlowAgentRequest request)
    {
        if (request.ReadOnlyDirectories is null)
            return null;
        if (request.ReadOnlyDirectories.Count == 0)
            throw new ArgumentException("Не заданы кодовые базы для анализа.", nameof(request));

        var root = request.WorkspaceDirectory.Replace('\\', '/').TrimEnd('/');
        var permissions = new List<PermissionRule>
        {
            new("*", "*", "deny"),
            new("read", ".", "allow"),
            new("glob", "*", "allow"),
            new("grep", "*", "allow")
        };

        foreach (var directory in request.ReadOnlyDirectories)
        {
            var path = directory.Replace('\\', '/').TrimEnd('/');
            if (!path.StartsWith(root + "/", StringComparison.Ordinal))
                throw new ArgumentException("Кодовая база находится вне контекста анализа.", nameof(request));
            var relative = path[(root.Length + 1)..];
            if (relative.Split('/').Any(segment => segment is ".." or "." or "" || segment.Contains('*') || segment.Contains('?')))
                throw new ArgumentException("Некорректный каталог кодовой базы.", nameof(request));

            permissions.Add(new("read", relative, "allow"));
            permissions.Add(new("read", relative + "/*", "allow"));
        }

        return permissions;
    }

    private async Task TryInterruptAsync(HttpClient client, string sessionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var response = await client.PostAsync($"api/session/{sessionId}/interrupt", null, timeout.Token);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("OpenCode did not interrupt session {SessionId}: {StatusCode}", sessionId, response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(exception, "Could not interrupt OpenCode session {SessionId}", sessionId);
        }
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
        [property: JsonPropertyName("location")] LocationReference Location,
        [property: JsonPropertyName("agent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Agent,
        [property: JsonPropertyName("permissions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PermissionRule>? Permissions);

    private sealed record PermissionRule(
        [property: JsonPropertyName("action")] string Action,
        [property: JsonPropertyName("resource")] string Resource,
        [property: JsonPropertyName("effect")] string Effect);

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
