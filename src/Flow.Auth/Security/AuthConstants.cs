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

    /// <summary>
    /// MCP-сервер Flow (Flow.Api/Mcp). Как и /api — только Bearer, cookie не принимается: клиент MCP — это
    /// агент или IDE, а не браузер, и 302 на форму входа ему показать некуда.
    /// </summary>
    public const string McpPathPrefix = "/mcp";

    /// <summary>
    /// Схема MCP из пакета ModelContextProtocol.AspNetCore (регистрирует Flow.Api): аутентификацию она отдаёт
    /// OpenIddict Validation, а на 401 добавляет в WWW-Authenticate адрес метаданных защищённого ресурса
    /// (RFC 9728) — по нему клиент MCP сам находит сервер авторизации.
    /// </summary>
    public const string McpScheme = "FlowMcp";

    /// <summary>
    /// Канонический адрес MCP-сервера (RFC 8707): его клиент MCP передаёт в resource и сверяет с метаданными
    /// защищённого ресурса. Строится от Auth:Issuer — того адреса, который видит клиент.
    /// </summary>
    public static string McpResource(string issuer) => issuer.TrimEnd('/') + McpPathPrefix;
}
