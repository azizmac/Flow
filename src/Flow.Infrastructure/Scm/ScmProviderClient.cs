using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Features.Scm;
using Flow.Domain.Entities;

namespace Flow.Infrastructure.Scm;

/// <summary>
/// API хостингов (docs/TZ_scm_integration.md §6): GitHub (api.github.com или {BaseUrl}/api/v3 у Enterprise), GitLab
/// ({BaseUrl}/api/v4), Gitea и Forgejo ({BaseUrl}/api/v1 — протокол общий). Токен — в заголовке каждого запроса,
/// базовый адрес у HttpClient не задан: подключений много, и у каждого свой. Ответ с ошибкой превращается в
/// ScmProviderException с понятной причиной: 401 — токен неверный, 403/404 — токену не хватает прав; исчерпанный лимит
/// запросов (429, или 403 с нулевым остатком / Retry-After) — ScmRateLimitException со временем сброса. У подключения
/// GitHub App вместо токена приходит закрытый ключ: токен установки клиент получает сам и кэширует (<see cref="GitHubAppTokens"/>).
/// </summary>
internal sealed class ScmProviderClient(IHttpClientFactory httpClients, GitHubAppTokens? appTokens = null) : IScmProviderClient
{
    private readonly GitHubAppTokens _appTokens = appTokens ?? new GitHubAppTokens();

    public const string HttpClientName = "scm";

    private static readonly string[] GitHubEvents = ["push", "pull_request", "create", "delete"];

    public async Task<string> CheckAsync(ScmConnection connection, string token, CancellationToken cancellationToken)
    {
        if (connection.AuthKind == ScmAuthKind.GitHubApp)
        {
            // Токен установки проверяет и ключ, и установку; имя приложения — по JWT самого приложения.
            await InstallationTokenAsync(connection, token, cancellationToken);
            using var app = await SendAsync(connection, token, HttpMethod.Get, "app", null, cancellationToken, GitHubAppJwt.Create(connection.AppId!.Value, token, DateTime.UtcNow));
            return $"{Str(app.RootElement, "slug") ?? "app"}[bot]";
        }

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
            ScmProvider.GitHub when connection.AuthKind == ScmAuthKind.GitHubApp => "installation/repositories?per_page=100",
            ScmProvider.GitHub => "user/repos?per_page=100&sort=updated",
            ScmProvider.GitLab => $"projects?membership=true&simple=true&per_page=50&order_by=last_activity_at&search={q}",
            _ => $"repos/search?limit=50&q={q}"
        };
        using var json = await SendAsync(connection, token, HttpMethod.Get, path, null, cancellationToken);
        var items = json.RootElement.ValueKind == JsonValueKind.Array
            ? json.RootElement.EnumerateArray()
            : json.RootElement.TryGetProperty("data", out var data) ? data.EnumerateArray()
            : json.RootElement.TryGetProperty("repositories", out var repos) ? repos.EnumerateArray() : default;

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

    public async Task<ScmHistory> GetHistoryAsync(ScmConnection connection, string token, ScmRepository repository, DateTime commitsSince,
        int maxPullRequests, int maxCommits, CancellationToken cancellationToken)
    {
        var since = Uri.EscapeDataString(DateTime.SpecifyKind(commitsSince, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        var branch = Uri.EscapeDataString(repository.DefaultBranch);
        var project = Uri.EscapeDataString(repository.ExternalId);
        // Gitea по умолчанию отдаёт не больше 50 на страницу (MAX_RESPONSE_ITEMS) — просим столько же, иначе «страница
        // короче запрошенной» ложно означала бы конец списка.
        var pageSize = connection.Provider is ScmProvider.Gitea or ScmProvider.Forgejo ? 50 : 100;

        Func<int, string> prs = connection.Provider switch
        {
            ScmProvider.GitHub => page => $"repos/{repository.FullName}/pulls?state=all&sort=updated&direction=desc&per_page={pageSize}&page={page}",
            ScmProvider.GitLab => page => $"projects/{project}/merge_requests?state=all&order_by=updated_at&sort=desc&per_page={pageSize}&page={page}",
            _ => page => $"repos/{repository.FullName}/pulls?state=all&sort=recentupdate&limit={pageSize}&page={page}"
        };
        Func<int, string> commits = connection.Provider switch
        {
            ScmProvider.GitLab => page => $"projects/{project}/repository/commits?ref_name={branch}&since={since}&per_page={pageSize}&page={page}",
            ScmProvider.GitHub => page => $"repos/{repository.FullName}/commits?sha={branch}&since={since}&per_page={pageSize}&page={page}",
            _ => page => $"repos/{repository.FullName}/commits?sha={branch}&since={since}&limit={pageSize}&page={page}"
        };

        var pullRequests = await PagesAsync(connection, token, prs, pageSize, maxPullRequests,
            e => ScmPayloadParser.ParsePullRequests(connection.Provider, e), null, cancellationToken);
        // Старый Gitea параметр since не знает — отсекаем сами и останавливаемся на первом коммите старше даты.
        var history = await PagesAsync(connection, token, commits, pageSize, maxCommits,
            e => ScmPayloadParser.ParseCommits(connection.Provider, e), c => c.Timestamp < commitsSince, cancellationToken);
        return new ScmHistory(pullRequests, history);
    }

    public async Task CreateBranchAsync(ScmConnection connection, string token, ScmRepository repository, string name, string fromBranch,
        CancellationToken cancellationToken)
    {
        switch (connection.Provider)
        {
            case ScmProvider.GitHub:
                // У GitHub нет «ветки от ветки»: берём sha головы исходной и создаём ref.
                using (var head = await SendAsync(connection, token, HttpMethod.Get,
                           $"repos/{repository.FullName}/git/ref/heads/{Uri.EscapeDataString(fromBranch)}", null, cancellationToken))
                {
                    var sha = head.RootElement.GetProperty("object").GetProperty("sha").GetString();
                    using var _ = await SendAsync(connection, token, HttpMethod.Post, $"repos/{repository.FullName}/git/refs",
                        new { @ref = $"refs/heads/{name}", sha }, cancellationToken);
                }
                break;
            case ScmProvider.GitLab:
                using (var _ = await SendAsync(connection, token, HttpMethod.Post,
                           $"projects/{Uri.EscapeDataString(repository.ExternalId)}/repository/branches?branch={Uri.EscapeDataString(name)}&ref={Uri.EscapeDataString(fromBranch)}",
                           null, cancellationToken))
                {
                }
                break;
            default:
                using (var _ = await SendAsync(connection, token, HttpMethod.Post, $"repos/{repository.FullName}/branches",
                           new { new_branch_name = name, old_branch_name = fromBranch }, cancellationToken))
                {
                }
                break;
        }
    }

    public async Task<ScmPullRequest> CreatePullRequestAsync(ScmConnection connection, string token, ScmRepository repository, string sourceBranch,
        string targetBranch, string title, string body, bool draft, CancellationToken cancellationToken)
    {
        (string Path, object Body) request = connection.Provider switch
        {
            ScmProvider.GitHub => ($"repos/{repository.FullName}/pulls", new { title, head = sourceBranch, @base = targetBranch, body, draft }),
            // У GitLab черновик — префикс заголовка «Draft:».
            ScmProvider.GitLab => ($"projects/{Uri.EscapeDataString(repository.ExternalId)}/merge_requests", new
            {
                source_branch = sourceBranch, target_branch = targetBranch, title = draft ? $"Draft: {title}" : title, description = body
            }),
            _ => ($"repos/{repository.FullName}/pulls", new { head = sourceBranch, @base = targetBranch, title = draft ? $"WIP: {title}" : title, body })
        };

        using var json = await SendAsync(connection, token, HttpMethod.Post, request.Path, request.Body, cancellationToken);
        using var list = JsonDocument.Parse($"[{json.RootElement.GetRawText()}]");
        return ScmPayloadParser.ParsePullRequests(connection.Provider, list.RootElement).FirstOrDefault()
               ?? throw new ScmProviderException("Хостинг не вернул созданный PR.");
    }

    public async Task CommentOnPullRequestAsync(ScmConnection connection, string token, ScmRepository repository, string number, string body,
        CancellationToken cancellationToken)
    {
        var path = connection.Provider == ScmProvider.GitLab
            ? $"projects/{Uri.EscapeDataString(repository.ExternalId)}/merge_requests/{number}/notes"
            : $"repos/{repository.FullName}/issues/{number}/comments";
        using var _ = await SendAsync(connection, token, HttpMethod.Post, path, new { body }, cancellationToken);
    }

    private async Task<List<T>> PagesAsync<T>(ScmConnection connection, string token, Func<int, string> path, int pageSize, int max,
        Func<JsonElement, IReadOnlyList<T>> parse, Func<T, bool>? tooOld, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        for (var page = 1; result.Count < max && page <= 50; page++)
        {
            using var json = await SendAsync(connection, token, HttpMethod.Get, path(page), null, cancellationToken);
            var items = parse(json.RootElement);
            foreach (var item in items)
            {
                if (tooOld?.Invoke(item) == true)
                    return result;
                result.Add(item);
                if (result.Count >= max)
                    return result;
            }

            if (items.Count < pageSize)
                break;
        }

        return result;
    }

    /// <summary>Токен установки GitHub App: из кэша, пока до истечения больше 5 минут, иначе новый по JWT приложения.</summary>
    private async Task<string> InstallationTokenAsync(ScmConnection connection, string privateKey, CancellationToken cancellationToken)
    {
        if (_appTokens.TryGet(connection, privateKey, DateTime.UtcNow) is { } cached)
            return cached;

        var jwt = GitHubAppJwt.Create(connection.AppId!.Value, privateKey, DateTime.UtcNow);
        using var json = await SendAsync(connection, privateKey, HttpMethod.Post, $"app/installations/{connection.InstallationId}/access_tokens", null,
            cancellationToken, jwt);
        var token = Str(json.RootElement, "token") ?? throw new ScmProviderException("GitHub не выдал токен установки.");
        var expires = DateTimeOffset.TryParse(Str(json.RootElement, "expires_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var e)
            ? e.UtcDateTime
            : DateTime.UtcNow.AddMinutes(50);
        _appTokens.Put(connection, privateKey, token, expires);
        return token;
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

    /// <param name="bearer">Готовый Bearer (JWT приложения) — мимо обычной подстановки токена.</param>
    private async Task<JsonDocument> SendAsync(ScmConnection connection, string token, HttpMethod method, string path, object? body,
        CancellationToken cancellationToken, string? bearer = null)
    {
        if (bearer is null && connection.AuthKind == ScmAuthKind.GitHubApp)
            bearer = await InstallationTokenAsync(connection, token, cancellationToken);

        using var request = new HttpRequestMessage(method, new Uri(new Uri(ApiRoot(connection)), path));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Flow", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        switch (connection.Provider)
        {
            case ScmProvider.GitHub:
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? token);
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
                if (RateLimitReset(response) is { } reset)
                    throw new ScmRateLimitException(reset);
                var reason = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized when bearer is not null && connection.AuthKind == ScmAuthKind.GitHubApp && path.StartsWith("app", StringComparison.Ordinal) =>
                        "GitHub не принял приложение: проверьте Id приложения и закрытый ключ.",
                    HttpStatusCode.NotFound when path.StartsWith("app/installations", StringComparison.Ordinal) =>
                        "Установка приложения не найдена — проверьте Id установки.",
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

    /// <summary>
    /// Лимит запросов: 429 всегда, 403 — если остаток нулевой или есть Retry-After (вторичный лимит GitHub). Сброс —
    /// Retry-After (секунды), иначе X-RateLimit-Reset / RateLimit-Reset (unix-время), иначе через минуту.
    /// </summary>
    internal static DateTime? RateLimitReset(HttpResponseMessage response)
    {
        string? Header(string name) =>
            response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

        var remaining = Header("X-RateLimit-Remaining") ?? Header("RateLimit-Remaining");
        var retryAfter = response.Headers.RetryAfter;
        var limited = response.StatusCode == HttpStatusCode.TooManyRequests
                      || (response.StatusCode == HttpStatusCode.Forbidden && (remaining == "0" || retryAfter is not null));
        if (!limited)
            return null;

        var now = DateTime.UtcNow;
        if (retryAfter?.Delta is { } delta)
            return now + delta;
        if (retryAfter?.Date is { } date)
            return date.UtcDateTime;
        if (long.TryParse(Header("X-RateLimit-Reset") ?? Header("RateLimit-Reset"), out var epoch) && epoch > 0)
            return DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return now.AddMinutes(1);
    }
}
