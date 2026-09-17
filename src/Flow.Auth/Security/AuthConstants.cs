namespace Flow.Auth.Security;

public static class AuthConstants
{
    /// <summary>Scope и одноимённый audience для Flow.Api: Flow.Api проверяет aud = flow-api.</summary>
    public const string ApiScope = "flow-api";

    public const string ApiResource = "flow-api";

    /// <summary>Политика страниц с cookie-схемой (смена пароля и т.п.).</summary>
    public const string CookiePolicy = "AuthCookie";
}
