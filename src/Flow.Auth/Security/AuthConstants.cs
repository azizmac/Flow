namespace Flow.Auth.Security;

public static class AuthConstants
{
    /// <summary>Scope и одноимённый audience для Flow.Api: Flow.Api проверяет aud = flow-api.</summary>
    public const string ApiScope = "flow-api";

    public const string ApiResource = "flow-api";

    /// <summary>Политика страниц с cookie-схемой (смена пароля и т.п.).</summary>
    public const string CookiePolicy = "AuthCookie";

    /// <summary>
    /// Схема-диспетчер: есть заголовок Authorization: Bearer — проверяем токен OpenIddict,
    /// нет — cookie flow.auth. Нужна из-за того, что в одном хосте теперь живут и JSON-API
    /// (Bearer), и серверные страницы Blazor (cookie), а схема по умолчанию может быть одна.
    /// </summary>
    public const string SmartScheme = "Flow";

    /// <summary>
    /// Префикс JSON-API (ApiPrefixConvention в Flow.Api). Схема-диспетчер по нему отличает запрос к API
    /// от запроса страницы: под этим префиксом cookie не принимается вовсе, только Bearer.
    /// </summary>
    public const string ApiPathPrefix = "/api";
}
