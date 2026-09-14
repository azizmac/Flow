namespace Flow.Api.Auth;

/// <summary>
/// Секция "Auth" в Flow.Api. BaseUrl — адрес для discovery/JWKS: Auth-модуль живёт в этом же процессе,
/// поэтому в Docker это self (http://localhost:8080); Issuer — внешний адрес, который модуль пишет в claim iss
/// (в Docker тот, что видит браузер). Если Issuer не задан, считается равным BaseUrl. Остальные поля секции
/// читает Auth-модуль (AuthOptions).
/// </summary>
public sealed class JwtAuthOptions
{
    public const string SectionName = "Auth";

    public const string Audience = "flow-api";

    public string BaseUrl { get; set; } = string.Empty;

    public string? Issuer { get; set; }

    /// <summary>Требовать https для discovery. null — только вне Development. В compose всё по http → false.</summary>
    public bool? RequireHttpsMetadata { get; set; }
}
