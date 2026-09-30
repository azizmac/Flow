using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Infrastructure.Scm;

/// <summary>
/// JWT приложения GitHub App (RS256): iss — Id приложения, iat на минуту в прошлом (часы GitHub и наши расходятся),
/// exp через 9 минут — GitHub принимает не больше 10. Им приложение меняет себя на токен установки.
/// </summary>
internal static class GitHubAppJwt
{
    public static string Create(long appId, string privateKeyPem, DateTime utcNow)
    {
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
        }
        catch (ArgumentException ex)
        {
            throw new ScmProviderException("Закрытый ключ приложения не читается — нужен PEM-файл из настроек GitHub App.", ex);
        }

        var now = new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var header = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { iat = now - 60, exp = now + 540, iss = appId.ToString() }));
        var signed = $"{header}.{payload}";
        var signature = rsa.SignData(Encoding.ASCII.GetBytes(signed), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signed}.{Base64Url(signature)}";
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Кэш токенов установок GitHub App (singleton): токен живёт час, и выписывать новый на каждый запрос значило бы
/// удвоить число обращений к GitHub. Ключ включает хеш закрытого ключа и Id — сменили ключ или установку, кэш не
/// отдаст старый токен.
/// </summary>
internal sealed class GitHubAppTokens
{
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> _tokens = new();

    public string? TryGet(GitHostConnection connection, string privateKey, DateTime utcNow) =>
        _tokens.TryGetValue(Key(connection, privateKey), out var entry) && entry.ExpiresAt - Margin > utcNow ? entry.Token : null;

    public void Put(GitHostConnection connection, string privateKey, string token, DateTime expiresAt) =>
        _tokens[Key(connection, privateKey)] = (token, expiresAt);

    private static string Key(GitHostConnection connection, string privateKey) =>
        $"{connection.Id:N}:{connection.AppId}:{connection.InstallationId}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(privateKey)))[..16]}";
}
