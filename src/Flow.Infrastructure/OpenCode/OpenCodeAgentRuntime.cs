using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flow.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Flow.Infrastructure.OpenCode;

/// <summary>
/// Конфигурация OpenCode через его HTTP API — без доступа к Docker. Поведение сверено с живым сервером (OpenAPI — GET /doc):
/// <list type="bullet">
/// <item>ключ провайдера из каталога — <c>POST /api/integration/{id}/connect/key</c> (хранилище учётных данных V2, его
/// читают сессии <c>/api/session</c>), снять — <c>DELETE /api/credential/{id}</c>; применяется сразу, без перезапуска.
/// Сервер без интеграций V2 (404) получает ключ по-старому — <c>PUT/DELETE /auth/{id}</c>;</item>
/// <item>кастомный провайдер — <c>PATCH /global/config</c> (адрес, модели и ключ — в его глобальной конфигурации).
/// Список провайдеров сессий OpenCode собирает на старте процесса, поэтому новый или изменённый кастомный провайдер
/// требует перезапуска OpenCode (<see cref="AgentApplyOutcome.RestartRequired"/>); удалить провайдера PATCH не умеет
/// (слияние), поэтому лишний попадает в <c>disabled_providers</c>;</item>
/// <item>подключённые — <c>GET /api/provider</c>, каталог — <c>GET /provider</c>, версия — <c>GET /global/health</c>.</item>
/// </list>
/// Каталоги OpenCode лежат в томе opencode-state (docker-compose.data.yml) и переживают пересоздание контейнера; если том
/// всё же потерян, OpenCodeConfigWorker передаст ключи заново.
/// </summary>
internal sealed class OpenCodeAgentRuntime(IHttpClientFactory clients, OpenCodeOptions options, ILogger<OpenCodeAgentRuntime> logger)
    : IAgentRuntime
{
    public const string HttpClientName = "flow-opencode-admin";
    private const string CompatiblePackage = "@ai-sdk/openai-compatible";
    private const string CompatiblePackageV2 = "@opencode/ai/providers/openai-compatible";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan WarmUpDelay = TimeSpan.FromMilliseconds(1500);

    /// <summary>Метка учётных данных, которые OpenCode получил от Flow: снимаются только они.</summary>
    public const string CredentialLabel = "Flow";

    private readonly SemaphoreSlim _catalogLock = new(1, 1);
    private (DateTime At, IReadOnlyList<AgentCatalogEntry> Entries)? _catalog;

    public AgentModelRef ConfiguredModel => new(options.ProviderId, options.ModelId);

    public async Task<AgentRuntimeState> GetStateAsync(CancellationToken cancellationToken)
    {
        using var timeout = Timeout(cancellationToken, StateTimeout);
        var client = clients.CreateClient(HttpClientName);
        try
        {
            var health = await GetJsonAsync(client, "global/health", "проверить состояние", timeout.Token);
            var connected = await ConnectedAsync(client, timeout.Token);
            if (connected.Count == 0)
            {
                // Только что стартовавший OpenCode первые секунды отдаёт пустой список, пока собирает провайдеров.
                await Task.Delay(WarmUpDelay, timeout.Token);
                connected = await ConnectedAsync(client, timeout.Token);
            }

            return new AgentRuntimeState(true, health?["version"]?.GetValue<string>(), null, connected);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
                                   || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new AgentRuntimeState(false, null, Describe(ex), new HashSet<string>());
        }
    }

    public async Task<IReadOnlyList<AgentCatalogEntry>> GetCatalogAsync(CancellationToken cancellationToken)
    {
        if (_catalog is { } cached && DateTime.UtcNow - cached.At < CatalogTtl)
            return cached.Entries;

        await _catalogLock.WaitAsync(cancellationToken);
        try
        {
            if (_catalog is { } again && DateTime.UtcNow - again.At < CatalogTtl)
                return again.Entries;

            using var timeout = Timeout(cancellationToken, RequestTimeout);
            var root = await Guarded(() => GetJsonAsync(clients.CreateClient(HttpClientName), "provider", "получить каталог провайдеров", timeout.Token),
                cancellationToken);
            var entries = (root?["all"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(Entry)
                .OfType<AgentCatalogEntry>()
                .ToList();
            _catalog = (DateTime.UtcNow, entries);
            return entries;
        }
        finally
        {
            _catalogLock.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, AgentApplyOutcome>> ApplyAsync(
        IReadOnlyList<AgentProviderConfig> providers,
        IReadOnlyCollection<string> removedKeys,
        CancellationToken cancellationToken)
    {
        var client = clients.CreateClient(HttpClientName);
        var results = providers.ToDictionary(p => p.ProviderKey, _ => AgentApplyOutcome.Ok, StringComparer.Ordinal);

        await Guarded(async () =>
        {
            HashSet<string> changed;
            using (var timeout = Timeout(cancellationToken, RequestTimeout))
                changed = await ApplyConfigAsync(client, providers, removedKeys, results, timeout.Token);

            foreach (var provider in providers.Where(p => !p.IsCustom && results[p.ProviderKey].Error is null))
            {
                using var timeout = Timeout(cancellationToken, RequestTimeout);
                var error = provider.ApiKey is { } key
                    ? await ConnectKeyAsync(client, provider.ProviderKey, key, timeout.Token)
                    : await RemoveKeyAsync(client, provider.ProviderKey, timeout.Token);
                if (error is not null)
                    results[provider.ProviderKey] = new AgentApplyOutcome(error);
            }

            foreach (var key in removedKeys.Where(k => !results.ContainsKey(k)))
            {
                using var timeout = Timeout(cancellationToken, RequestTimeout);
                if (await RemoveKeyAsync(client, key, timeout.Token) is { } error)
                    logger.LogWarning("OpenCode did not remove credentials of {Provider}: {Error}", key, error);
            }

            // Кастомный провайдер сессии увидят только после перезапуска OpenCode: изменился или ещё не виден — отметить.
            var custom = providers.Where(p => p.IsCustom && results[p.ProviderKey].Error is null).ToList();
            if (custom.Count > 0)
            {
                using var timeout = Timeout(cancellationToken, RequestTimeout);
                var connected = await ConnectedAsync(client, timeout.Token);
                foreach (var provider in custom)
                    results[provider.ProviderKey] = new AgentApplyOutcome(null,
                        changed.Contains(provider.ProviderKey) || !connected.Contains(provider.ProviderKey));
            }

            return true;
        }, cancellationToken);

        // Кастомные провайдеры появляются в каталоге OpenCode — следующий список должен их увидеть.
        _catalog = null;
        return results;
    }

    public async Task<string> CheckAsync(AgentModelRef model, CancellationToken cancellationToken)
    {
        // Та же последовательность, что у OpenCodeClient, но без доступа к файлам: агент plan, все инструменты запрещены.
        var client = clients.CreateClient(HttpClientName);
        return await Guarded(async () =>
        {
            using var created = await client.PostAsJsonAsync("api/session", new
            {
                title = "Проверка модели Flow",
                model = new { providerID = model.ProviderKey, id = model.ModelId },
                location = new { directory = options.CheckDirectory },
                agent = "plan",
                permissions = new[] { new { action = "*", resource = "*", effect = "deny" } }
            }, cancellationToken);
            if (!created.IsSuccessStatusCode)
                throw new HttpRequestException(await FailureAsync(created, "создать сессию", cancellationToken), null, created.StatusCode);
            var session = (await created.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))?["data"]?["id"]?.GetValue<string>()
                          ?? throw new InvalidOperationException("OpenCode не вернул созданную сессию.");

            try
            {
                using (var prompt = await client.PostAsJsonAsync($"api/session/{session}/prompt", new { text = "Ответь одним словом: ok" }, cancellationToken))
                    if (!prompt.IsSuccessStatusCode)
                        throw new HttpRequestException(await FailureAsync(prompt, "принять запрос", cancellationToken), null, prompt.StatusCode);

                using (var wait = await client.PostAsync($"api/session/{session}/wait", null, cancellationToken))
                    if (!wait.IsSuccessStatusCode)
                        throw new HttpRequestException(await FailureAsync(wait, "получить ответ модели", cancellationToken), null, wait.StatusCode);

                var messages = await client.GetFromJsonAsync<JsonObject>($"api/session/{session}/message?type=assistant&order=desc&limit=1", cancellationToken);
                var message = (messages?["data"] as JsonArray)?.FirstOrDefault() as JsonObject;
                var text = string.Join(" ", (message?["content"] as JsonArray ?? [])
                    .OfType<JsonObject>()
                    .Where(part => part["type"]?.GetValue<string>() == "text")
                    .Select(part => part["text"]?.GetValue<string>())
                    .Where(t => !string.IsNullOrWhiteSpace(t)));
                if (!string.IsNullOrWhiteSpace(text))
                    return text;

                throw new HttpRequestException(ErrorOf(message) is { } error
                    ? $"Провайдер отказал: {error}"
                    : "Модель не вернула текст — проверьте ключ и Id модели.");
            }
            catch (OperationCanceledException)
            {
                await TryInterruptAsync(client, session);
                throw;
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Кастомные провайдеры — в глобальную конфигурацию, лишние — в disabled_providers. Результат — кастомные, чья
    /// конфигурация в OpenCode изменилась этой передачей (им нужен перезапуск OpenCode).
    /// </summary>
    private async Task<HashSet<string>> ApplyConfigAsync(HttpClient client, IReadOnlyList<AgentProviderConfig> providers,
        IReadOnlyCollection<string> removedKeys, Dictionary<string, AgentApplyOutcome> results, CancellationToken cancellationToken)
    {
        var current = await GetJsonAsync(client, "global/config", "прочитать конфигурацию", cancellationToken);
        var desired = providers.Select(p => p.ProviderKey).ToHashSet(StringComparer.Ordinal);
        var existing = current?["provider"] as JsonObject ?? current?["providers"] as JsonObject;
        var disabled = (current?["disabled_providers"] as JsonArray ?? [])
            .Select(p => p?.GetValue<string>())
            .OfType<string>()
            .Concat(existing?.Select(p => p.Key) ?? [])
            .Concat(removedKeys)
            .Where(k => !desired.Contains(k))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var custom = providers.Where(p => p.IsCustom).ToList();
        var blocks = custom.ToDictionary(p => p.ProviderKey, p => ProviderV1(p, existing?[p.ProviderKey]), StringComparer.Ordinal);
        var changed = custom.Where(p => !Covers(existing?[p.ProviderKey], blocks[p.ProviderKey])).Select(p => p.ProviderKey).ToHashSet(StringComparer.Ordinal);

        var body = new JsonObject { ["disabled_providers"] = new JsonArray(disabled.Select(k => (JsonNode)k).ToArray()) };
        if (custom.Count > 0)
            body["provider"] = new JsonObject(blocks.Select(b => KeyValuePair.Create(b.Key, (JsonNode?)b.Value)));

        using var response = await client.PatchAsJsonAsync("global/config", body, cancellationToken);
        if (response.IsSuccessStatusCode)
            return changed;

        // Конфигурация V2 (`providers` + `package`/`settings`, как в docker/opencode/opencode.jsonc) — вторая попытка.
        if (response.StatusCode == HttpStatusCode.BadRequest && custom.Count > 0)
        {
            body.Remove("provider");
            body["providers"] = new JsonObject(custom.Select(p => KeyValuePair.Create(p.ProviderKey, (JsonNode?)ProviderV2(p, existing?[p.ProviderKey]))));
            using var retry = await client.PatchAsJsonAsync("global/config", body, cancellationToken);
            if (retry.IsSuccessStatusCode)
                return custom.Select(p => p.ProviderKey).ToHashSet(StringComparer.Ordinal);
            var error = await FailureAsync(retry, "принять кастомного провайдера", cancellationToken);
            foreach (var p in custom)
                results[p.ProviderKey] = new AgentApplyOutcome(error);
            return [];
        }

        var failure = await FailureAsync(response, "обновить конфигурацию", cancellationToken);
        foreach (var p in custom)
            results[p.ProviderKey] = new AgentApplyOutcome(failure);
        return [];
    }

    /// <summary>
    /// Ключ — в хранилище учётных данных V2 под меткой Flow (повторное подключение заменяет прежний). Сервер без
    /// интеграций V2 — по-старому, в auth.json. null — принят.
    /// </summary>
    private static async Task<string?> ConnectKeyAsync(HttpClient client, string providerKey, string key, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(providerKey);
        using var response = await client.PostAsJsonAsync($"api/integration/{id}/connect/key", new { key, label = CredentialLabel }, cancellationToken);
        if (response.IsSuccessStatusCode)
            return null;
        if (response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed))
            return await FailureAsync(response, "принять ключ", cancellationToken);

        using var legacy = await client.PutAsJsonAsync($"auth/{id}", new { type = "api", key }, cancellationToken);
        return legacy.IsSuccessStatusCode ? null : await FailureAsync(legacy, "принять ключ", cancellationToken);
    }

    /// <summary>Снять ключи Flow у провайдера: учётные данные V2 с меткой Flow и запись auth.json. null — сняты или их не было.</summary>
    private static async Task<string?> RemoveKeyAsync(HttpClient client, string providerKey, CancellationToken cancellationToken)
    {
        var id = Uri.EscapeDataString(providerKey);
        using (var legacy = await client.DeleteAsync($"auth/{id}", cancellationToken))
        {
            // Старое хранилище — best-effort: на сервере без него 404, и это не ошибка.
        }

        using var response = await client.GetAsync($"api/integration/{id}", cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            return null;
        if (!response.IsSuccessStatusCode)
            return await FailureAsync(response, "прочитать ключи провайдера", cancellationToken);

        var integration = (await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))?["data"];
        var credentials = (integration?["connections"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Where(c => c["type"]?.GetValue<string>() == "credential" && c["label"]?.GetValue<string>() == CredentialLabel)
            .Select(c => c["id"]?.GetValue<string>())
            .OfType<string>();
        foreach (var credential in credentials)
        {
            using var removed = await client.DeleteAsync($"api/credential/{Uri.EscapeDataString(credential)}", cancellationToken);
            if (!removed.IsSuccessStatusCode && removed.StatusCode != HttpStatusCode.NotFound)
                return await FailureAsync(removed, "снять ключ", cancellationToken);
        }

        return null;
    }

    /// <summary>Провайдеры, которые видят сессии: V2-список; на сервере без него — «connected» старого API.</summary>
    private static async Task<HashSet<string>> ConnectedAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync("api/provider", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var legacy = await GetJsonAsync(client, "provider", "получить подключённых провайдеров", cancellationToken);
            return (legacy?["connected"] as JsonArray ?? []).Select(p => p?.GetValue<string>()).OfType<string>().ToHashSet(StringComparer.Ordinal);
        }

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await FailureAsync(response, "получить подключённых провайдеров", cancellationToken), null, response.StatusCode);
        var root = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return (root?["data"] as JsonArray ?? [])
            .Select(p => p?["id"]?.GetValue<string>())
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Всё из <paramref name="desired"/> уже есть в <paramref name="existing"/> с теми же значениями (PATCH сливает, лишнее не мешает).</summary>
    private static bool Covers(JsonNode? existing, JsonNode? desired) => (existing, desired) switch
    {
        (_, null) => true,
        (JsonObject have, JsonObject want) => want.All(p => Covers(have[p.Key], p.Value)),
        (null, _) => false,
        _ => existing.ToJsonString() == desired.ToJsonString()
    };

    /// <summary>Ключ кастомного провайдера — в options.apiKey; ключ сняли, а в OpenCode он был — затереть пустым (удалить PATCH не умеет).</summary>
    private static JsonObject ProviderV1(AgentProviderConfig p, JsonNode? existing)
    {
        var options = new JsonObject { ["baseURL"] = p.BaseUrl };
        if (p.ApiKey is { } key)
            options["apiKey"] = key;
        else if (existing?["options"]?["apiKey"] is not null)
            options["apiKey"] = "";
        return new JsonObject
        {
            ["name"] = p.Name,
            ["npm"] = CompatiblePackage,
            ["options"] = options,
            ["models"] = new JsonObject(p.Models.Select(m => KeyValuePair.Create(m.Id, (JsonNode?)new JsonObject { ["name"] = m.Name })))
        };
    }

    private static JsonObject ProviderV2(AgentProviderConfig p, JsonNode? existing)
    {
        var settings = new JsonObject { ["baseURL"] = p.BaseUrl };
        if (p.ApiKey is { } key)
            settings["apiKey"] = key;
        else if (existing?["settings"]?["apiKey"] is not null)
            settings["apiKey"] = "";
        return new JsonObject
        {
            ["name"] = p.Name,
            ["package"] = CompatiblePackageV2,
            ["settings"] = settings,
            ["models"] = new JsonObject(p.Models.Select(m => KeyValuePair.Create(m.Id, (JsonNode?)new JsonObject { ["name"] = m.Name, ["modelID"] = m.Id })))
        };
    }

    private static AgentCatalogEntry? Entry(JsonObject provider)
    {
        if (provider["id"]?.GetValue<string>() is not { Length: > 0 } id)
            return null;
        var name = provider["name"]?.GetValue<string>() ?? id;
        var env = (provider["env"] as JsonArray ?? []).Select(e => e?.GetValue<string>()).OfType<string>().ToList();
        var models = (provider["models"] as JsonObject ?? [])
            .Select(m => (Id: m.Value?["id"]?.GetValue<string>() ?? m.Key, Name: m.Value?["name"]?.GetValue<string>() ?? m.Key))
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new AgentCatalogEntry(id, name, env, models);
    }

    /// <summary>Текст ошибки из сообщения ассистента, если провайдер отказал (ключ, квота, модель).</summary>
    private static string? ErrorOf(JsonObject? message)
    {
        var error = message?["error"];
        return error switch
        {
            null => null,
            JsonValue value => value.ToString(),
            _ => error["message"]?.ToString() ?? error["data"]?["message"]?.ToString() ?? error.ToJsonString()
        };
    }

    private static async Task<JsonObject?> GetJsonAsync(HttpClient client, string path, string action, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await FailureAsync(response, action, cancellationToken), null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
    }

    private static async Task<string> FailureAsync(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return $"OpenCode не смог {action}: 401 — пароль OpenCode у Flow и у контейнера не совпадает (OPENCODE_SERVER_PASSWORD).";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var detail = body.Length <= 300 ? body : body[..300] + "…";
        return $"OpenCode не смог {action}: {(int)response.StatusCode} {detail}".TrimEnd();
    }

    /// <summary>Сбои связи и разбора — в HttpRequestException/TimeoutException, как обещает IAgentRuntime.</summary>
    private static async Task<T> Guarded<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("OpenCode не ответил вовремя.", ex);
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException("OpenCode вернул некорректный ответ.", ex);
        }
    }

    private async Task TryInterruptAsync(HttpClient client, string sessionId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            using var _ = await client.PostAsync($"api/session/{sessionId}/interrupt", null, timeout.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not interrupt OpenCode check session {SessionId}", sessionId);
        }
    }

    private static CancellationTokenSource Timeout(CancellationToken cancellationToken, TimeSpan after)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(after);
        return source;
    }

    private string Describe(Exception ex) => ex switch
    {
        OperationCanceledException => $"OpenCode ({options.BaseUrl}) не ответил за {StateTimeout.TotalSeconds:0} с.",
        HttpRequestException { StatusCode: null } => $"OpenCode ({options.BaseUrl}) недоступен: {ex.Message}",
        _ => ex.Message
    };
}
