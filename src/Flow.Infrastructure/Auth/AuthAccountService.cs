using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Shared.Contracts.Accounts;
using Microsoft.Extensions.Options;

namespace Flow.Infrastructure.Auth;

/// <summary>
/// HTTP-клиент admin-API Flow.Auth (/accounts). Токен flow-api берёт у <see cref="ClientCredentialsTokenProvider"/>.
/// Маппинг ответов: 2xx → Success; 409 → UsernameTaken/EmailTaken (по тексту Identity); 400/404 → Invalid;
/// 401 (секрет/ключи сменились), 5xx и сетевые ошибки → AuthUnavailableException (→ 502).
/// </summary>
public sealed class AuthAccountService(HttpClient http, ClientCredentialsTokenProvider tokens, IOptions<AuthClientOptions> options)
    : IAccountService
{
    public Task<AccountResult> CreateAsync(Guid id, string username, string email, string password, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, "accounts", new CreateAccountRequest(id, username, email, password), cancellationToken);

    public Task<AccountResult> ChangeUsernameAsync(Guid id, string username, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, $"accounts/{id}/username", new ChangeAccountUsernameRequest(username), cancellationToken);

    public Task<AccountResult> ChangeEmailAsync(Guid id, string email, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Patch, $"accounts/{id}/email", new ChangeAccountEmailRequest(email), cancellationToken);

    public Task<AccountResult> ChangePasswordAsync(Guid id, string? currentPassword, string newPassword, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, $"accounts/{id}/password", new ChangeAccountPasswordRequest(currentPassword, newPassword), cancellationToken);

    public async Task DisableAsync(Guid id, CancellationToken cancellationToken) =>
        EnsureSuccess(await SendAsync(HttpMethod.Post, $"accounts/{id}/disable", body: null, cancellationToken), "disable");

    public async Task EnableAsync(Guid id, CancellationToken cancellationToken) =>
        EnsureSuccess(await SendAsync(HttpMethod.Post, $"accounts/{id}/enable", body: null, cancellationToken), "enable");

    private async Task<AccountResult> SendAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        var baseUrl = options.Value.BaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new AuthUnavailableException("Auth:BaseUrl is not configured.");

        http.BaseAddress ??= new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        try
        {
            using var response = await SendWithTokenAsync(method, url, body, cancellationToken);

            if (response.IsSuccessStatusCode)
                return AccountResult.Success();

            var message = await ReadMessageAsync(response, cancellationToken);
            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => message.Contains("email", StringComparison.OrdinalIgnoreCase)
                    ? AccountResult.EmailTaken(message)
                    : AccountResult.UsernameTaken(message),
                HttpStatusCode.BadRequest => AccountResult.Invalid(message),
                HttpStatusCode.NotFound => AccountResult.Invalid("Account not found in Flow.Auth."),
                _ => throw new AuthUnavailableException($"Flow.Auth responded {(int)response.StatusCode} to {method} {url}: {message}")
            };
        }
        catch (HttpRequestException ex)
        {
            throw new AuthUnavailableException($"Flow.Auth is unreachable at {baseUrl}: {ex.Message}", ex);
        }
    }

    /// <summary>При 401 один раз обновляет токен и повторяет запрос: токен мог устареть вместе с ключами Flow.Auth.</summary>
    private async Task<HttpResponseMessage> SendWithTokenAsync(HttpMethod method, string url, object? body, CancellationToken cancellationToken)
    {
        var response = await SendOnceAsync(method, url, body, await tokens.GetTokenAsync(cancellationToken), cancellationToken);
        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();
        tokens.Invalidate();
        return await SendOnceAsync(method, url, body, await tokens.GetTokenAsync(cancellationToken), cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string url, object? body, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType());

        return await http.SendAsync(request, cancellationToken);
    }

    private static void EnsureSuccess(AccountResult result, string operation)
    {
        if (!result.IsSuccess)
            throw new AuthUnavailableException($"Flow.Auth could not {operation} the account: {result.Error}");
    }

    private static async Task<string> ReadMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
            return $"HTTP {(int)response.StatusCode}";

        try
        {
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
                return message.GetString()!;
        }
        catch (JsonException)
        {
            // не JSON — вернём тело как есть
        }

        return text;
    }
}
