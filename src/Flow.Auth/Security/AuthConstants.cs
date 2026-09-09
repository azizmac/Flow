namespace Flow.Auth.Security;

public static class AuthConstants
{
    /// <summary>Scope и одноимённый audience для Flow.Api: Flow.Api проверяет aud = flow-api.</summary>
    public const string ApiScope = "flow-api";

    public const string ApiResource = "flow-api";

    /// <summary>Scope admin-API этого сервиса (client_credentials клиента flow-api).</summary>
    public const string AdminScope = "auth:admin";

    public const string AuthResource = "flow-auth";

    /// <summary>Политика авторизации admin-API: Bearer-токен с aud = flow-auth и scope auth:admin.</summary>
    public const string AdminPolicy = "AuthAdmin";
}
