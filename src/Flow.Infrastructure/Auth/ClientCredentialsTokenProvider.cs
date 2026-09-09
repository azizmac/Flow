using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Flow.Application.Exceptions;
using Microsoft.Extensions.Options;

namespace Flow.Infrastructure.Auth;

/// <summary>
/// Получает access token клиента flow-api у Flow.Auth (POST /connect/token, grant_type=client_credentials)
/// и кэширует его до истечения минус 30 секунд. Singleton: один токен на процесс, обновление под семафором.
/// </summary>
public sealed class ClientCredentialsTokenProvider(IHttpClientFactory httpClientFactory, IOptions<AuthClientOptions> options)
{
    public const string HttpClientName = "FlowAuth.Token";

    private static readonly TimeSpan ExpirySkew = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
            return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _expiresAt)
                return _token;

            var response = await RequestTokenAsync(cancellationToken);
            _token = response.AccessToken;
            _expiresAt = DateTimeOffset.UtcNow.AddSeconds(response.ExpiresIn) - ExpirySkew;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Сбросить кэш — после 401 от admin-API (секрет или ключи Flow.Auth сменились).</summary>
    public void Invalidate() => _token = null;

    private async Task<TokenResponse> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var auth = options.Value;
        if (string.IsNullOrWhiteSpace(auth.BaseUrl))
            throw new AuthUnavailableException("Auth:BaseUrl is not configured.");

        if (string.IsNullOrWhiteSpace(auth.ApiClient.Secret))
            throw new AuthUnavailableException("Auth:ApiClient:Secret is not configured.");

        using var http = httpClientFactory.CreateClient(HttpClientName);
        http.BaseAddress = new Uri(auth.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        try
        {
            using var response = await http.PostAsync("connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = auth.ApiClient.ClientId,
                ["client_secret"] = auth.ApiClient.Secret,
                ["scope"] = auth.ApiClient.Scope
            }), cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new AuthUnavailableException($"Flow.Auth refused the client credentials ({(int)response.StatusCode}): {body}");
            }

            return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
                ?? throw new AuthUnavailableException("Flow.Auth returned an empty token response.");
        }
        catch (HttpRequestException ex)
        {
            throw new AuthUnavailableException($"Flow.Auth is unreachable at {auth.BaseUrl}: {ex.Message}", ex);
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
