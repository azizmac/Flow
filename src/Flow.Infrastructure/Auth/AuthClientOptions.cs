namespace Flow.Infrastructure.Auth;

/// <summary>
/// Секция "Auth" в Flow.Api: адрес Flow.Auth для служебных вызовов и клиент flow-api (client_credentials, scope auth:admin).
/// В Docker BaseUrl — внутренний адрес (http://auth:8080); внешний issuer токенов задаётся отдельно (#24, JwtBearer).
/// </summary>
public sealed class AuthClientOptions
{
    public const string SectionName = "Auth";

    public string BaseUrl { get; set; } = string.Empty;

    public ApiClientOptions ApiClient { get; set; } = new();

    public sealed class ApiClientOptions
    {
        public string ClientId { get; set; } = "flow-api";

        public string Secret { get; set; } = string.Empty;

        public string Scope { get; set; } = "auth:admin";
    }
}
