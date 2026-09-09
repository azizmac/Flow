using System.Text.Json;

namespace Flow.Auth.Tests;

/// <summary>Читает payload JWT без проверки подписи — для ассертов на claims в тестах.</summary>
public static class Jwt
{
    public static JsonElement Payload(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

        return JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.Clone();
    }
}
