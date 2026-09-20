namespace Flow.Auth.Options;

/// <summary>Секция "Auth" в appsettings / env (Auth__Issuer, Auth__Client__RedirectUris__0, ...). См. docs/TZ_modular_monolith.md.</summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>Внешний адрес backend — попадает в claim iss и в discovery. В Docker — тот, что видит браузер.</summary>
    public string Issuer { get; set; } = "http://localhost:5000";

    public ClientOptions Client { get; set; } = new();

    public BCryptOptions BCrypt { get; set; } = new();

    public int AccessTokenLifetimeMinutes { get; set; } = 60;

    public int RefreshTokenLifetimeDays { get; set; } = 14;

    /// <summary>Разрешить http для эндпоинтов OpenIddict вне Development (локальный Docker без TLS).</summary>
    public bool AllowInsecureHttp { get; set; }

    /// <summary>Ключи в памяти без сертификатов — только для автотестов; после перезапуска все токены недействительны.</summary>
    public bool UseEphemeralKeys { get; set; }

    /// <summary>PFX для подписи access/id token. Обязателен вне Development (в Development — dev-сертификат OpenIddict).</summary>
    public CertificateOptions SigningCertificate { get; set; } = new();

    /// <summary>PFX для шифрования кодов и refresh-токенов. Обязателен вне Development.</summary>
    public CertificateOptions EncryptionCertificate { get; set; } = new();

    /// <summary>
    /// Публичный OIDC-клиент (code + PKCE). Интерфейс им больше не пользуется — при серверном рендере
    /// вход идёт через /account/login и cookie. Оставлен ради отката на старый образ WASM-клиента.
    /// </summary>
    public sealed class ClientOptions
    {
        public string ClientId { get; set; } = "flow-client";

        public string[] RedirectUris { get; set; } = [];

        public string[] PostLogoutRedirectUris { get; set; } = [];
    }

    public sealed class BCryptOptions
    {
        public int WorkFactor { get; set; } = 12;
    }

    public sealed class CertificateOptions
    {
        public string? Path { get; set; }

        public string? Password { get; set; }
    }
}
