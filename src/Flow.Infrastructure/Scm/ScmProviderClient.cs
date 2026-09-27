using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Infrastructure.Scm;

/// <summary>
/// API хостингов (docs/TZ_scm_integration.md §6): GitHub (api.github.com или {BaseUrl}/api/v3 у Enterprise), GitLab
/// ({BaseUrl}/api/v4), Gitea и Forgejo ({BaseUrl}/api/v1 — протокол общий). Токен — в заголовке каждого запроса,
/// базовый адрес у HttpClient не задан: подключений много, и у каждого свой. Ответ с ошибкой превращается в
/// ScmProviderException с понятной причиной: 401 — токен неверный, 403/404 — токену не хватает прав.
/// </summary>
internal sealed class ScmProviderClient(IHttpClientFactory httpClients) : IScmProviderClient
{
    public const string HttpClientName = "scm";

    private static readonly string[] GitHubEvents = ["push", "pull_request", "create", "delete"];

    public async Task<string> CheckAsync(ScmConnection connection, string token, CancellationToken cancellationToken)
    {
        using var json = await SendAsync(connection, token, HttpMethod.Get, "user", null, cancellationToken);
        var root = json.RootElement;
        return (root.TryGetProperty("login", out var login) ? login.GetString() : root.TryGetProperty("username", out var username) ? username.GetString() : null)
               ?? "?";
    }

    public async Task<IReadOnlyList<ScmRemoteRepository>> ListRepositoriesAsync(ScmConnection connection, string token, string? query, CancellationToken cancellationToken)
    {
        var q = Uri.EscapeDataString(query?.Trim() ?? "");
        var path = connection.Provider switch
        {
            ScmProvider.GitHub => "user/repos?per_page=100&sort=updated",
            ScmProvider.GitLab => $"projects?membership=true&simple=true&per_page=50&order_by=last_activity_at&search={q}",
            _ => $"repos/search?limit=50&q={q}"
        };
        using var json = await SendAsync(connection, token, HttpMethod.Get, path, null, cancellationToken);
        var items = json.RootElement.ValueKind == JsonValueKind.Array
            ? json.RootElement.EnumerateArray()
            : json.RootElement.TryGetProperty("data", out var data) ? data.EnumerateArray() : default;

        var result = items.Select(e => Map(connection.Provider, e)).ToList();
        // GitHub не ищет по списку своих репозиториев — фильтруем сами.
        return string.IsNullOrWhiteSpace(query)
            ? result
            : result.Where(r => r.FullName.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task<ScmRemoteRepository?> GetRepositoryAsync(ScmConnection connection, string token, string externalId, CancellationToken cancellationToken)
    {
        var path = connection.Provider switch
        {
            ScmProvider.GitHub => $"repositories/{Uri.EscapeDataString(externalId)}",
            ScmProvider.GitLab => $"projects/{Uri.EscapeDataString(externalId)}",
            _ => $"repositories/{Uri.EscapeDataString(externalId)}"
        };
        try
        {
            using var json = await SendAsync(connection, token, HttpMethod.Get, path, null, cancellationToken);
            return Map(connection.Provider, json.RootElement);
        }
        catch (ScmProviderException ex) when (ex.InnerException is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            return null;
        }
    }

    public async Task<string> CreateWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string url, string secret, CancellationToken cancellationToken)
    {
        (string Path, object Body) request = connection.Provider switch
        {
            ScmProvider.GitHub => ($"repos/{repository.FullName}/hooks", new
            {
                name = "web",
                active = true,
                events = GitHubEvents,
                config = new { url, content_type = "json", secret, insecure_ssl = "0" }
            }),
            ScmProvider.GitLab => ($"projects/{Uri.EscapeDataString(repository.ExternalId)}/hooks", new
            {
                url,
                token = secret,
                push_events = true,
                merge_requests_events = true,
                enable_ssl_verification = true
            }),
            _ => ($"repos/{repository.FullName}/hooks", new
            {
                type = connection.Provider == ScmProvider.Forgejo ? "forgejo" : "gitea",
                active = true,
                events = GitHubEvents,
                config = new { url, content_type = "json", secret }
            })
        };

        using var json = await SendAsync(connection, token, HttpMethod.Post, request.Path, request.Body, cancellationToken);
        return json.RootElement.GetProperty("id").GetRawText();
    }

    public async Task DeleteWebhookAsync(ScmConnection connection, string token, ScmRepository repository, string webhookId, CancellationToken cancellationToken)
    {
        var path = connection.Provider == ScmProvider.GitLab
            ? $"projects/{Uri.EscapeDataString(repository.ExternalId)}/hooks/{webhookId}"
            : $"repos/{repository.FullName}/hooks/{webhookId}";
        using var _ = await SendAsync(connection, token, HttpMethod.Delete, path, null, cancellationToken);
    }

    /// <summary>Корень API провайдера.</summary>
    public static string ApiRoot(ScmConnection connection) => connection.Provider switch
    {
        ScmProvider.GitHub => connection.BaseUrl is null ? "https://api.github.com/" : $"{connection.BaseUrl}/api/v3/",
        ScmProvider.GitLab => $"{connection.BaseUrl}/api/v4/",
        _ => $"{connection.BaseUrl}/api/v1/"
    };

    private static ScmRemoteRepository Map(ScmProvider provider, JsonElement e) => provider == ScmProvider.GitLab
        ? new ScmRemoteRepository(e.GetProperty("id").GetRawText(), Str(e, "path_with_namespace") ?? "", Str(e, "web_url") ?? "", Str(e, "default_branch"))
        : new ScmRemoteRepository(e.GetProperty("id").GetRawText(), Str(e, "full_name") ?? "", Str(e, "html_url") ?? "", Str(e, "default_branch"));

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private async Task<JsonDocument> SendAsync(ScmConnection connection, string token, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot(connection)), path));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Flow", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        switch (connection.Provider)
        {
            case ScmProvider.GitHub:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                break;
            case ScmProvider.GitLab:
                request.Headers.Add("PRIVATE-TOKEN", token);
                break;
            default:
                request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                break;
        }
        // С Content-Length, а не чанками (как JsonContent): простые прокси и самописные хостинги чанки не любят.
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ScmProviderException($"Хостинг недоступен: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ScmProviderException("Хостинг не ответил вовремя.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var reason = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Токен не принят хостингом (неверный или отозван).",
                    HttpStatusCode.Forbidden => "Токену не хватает прав на это действие.",
                    HttpStatusCode.NotFound => "Не найдено — или токену не хватает прав это видеть.",
                    _ => $"Хостинг ответил {(int)response.StatusCode}."
                };
                throw new ScmProviderException(reason, new HttpRequestException(reason, null, response.StatusCode));
            }

            if (response.StatusCode == HttpStatusCode.NoContent || response.Content.Headers.ContentLength == 0)
                return JsonDocument.Parse("{}");

            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }
    }
}
