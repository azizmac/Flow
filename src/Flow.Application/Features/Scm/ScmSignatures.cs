using System.Security.Cryptography;
using System.Text;
using Flow.Domain.Entities;

namespace Flow.Application.Features.Scm;

/// <summary>
/// Проверка подписи вебхука по сырому телу, до разбора JSON (docs/TZ_scm_integration.md §2). Сравнение — за
/// постоянное время: иначе по задержке ответа можно подбирать подпись байт за байтом.
/// GitHub — «sha256=hex» в X-Hub-Signature-256, Gitea/Forgejo — hex в своём заголовке, GitLab — сам секрет в X-Gitlab-Token.
/// </summary>
public static class ScmSignatures
{
    public static bool Verify(ScmProvider provider, Func<string, string?> header, ReadOnlySpan<byte> body, string secret)
    {
        switch (provider)
        {
            case ScmProvider.GitLab:
            {
                var token = header("X-Gitlab-Token");
                return token is not null && FixedEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(secret));
            }
            case ScmProvider.GitHub:
            {
                var signature = header("X-Hub-Signature-256");
                return signature is not null && signature.StartsWith("sha256=", StringComparison.Ordinal) && HexEquals(signature[7..], body, secret);
            }
            default:
            {
                var signature = header("X-Forgejo-Signature") ?? header("X-Gitea-Signature");
                return signature is not null && HexEquals(signature, body, secret);
            }
        }
    }

    /// <summary>Подпись тела — для тестов и ручной проверки: hex HMAC-SHA256.</summary>
    public static string Sign(ReadOnlySpan<byte> body, string secret) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    private static bool HexEquals(string hex, ReadOnlySpan<byte> body, string secret)
    {
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(hex.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return FixedEquals(expected, HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
    }

    private static bool FixedEquals(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
}
