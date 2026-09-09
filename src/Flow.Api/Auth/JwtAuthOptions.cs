namespace Flow.Api.Auth;

/// <summary>
/// Секция "Auth" в Flow.Api. BaseUrl — адрес Flow.Auth для discovery/JWKS и admin-API (в Docker внутренний,
/// http://auth:8080); Issuer — внешний адрес, который Flow.Auth пишет в claim iss (в Docker тот, что видит браузер).
/// Если Issuer не задан, считается равным BaseUrl. Остальные поля секции читает Flow.Infrastructure (AuthClientOptions).
/// </summary>
public sealed class JwtAuthOptions
{
    public const string SectionName = "Auth";

    public const string Audience = "flow-api";

    public string BaseUrl { get; set; } = string.Empty;

    public string? Issuer { get; set; }
}
