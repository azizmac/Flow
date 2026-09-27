using System.Security.Cryptography;
using Flow.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Flow.Api.Scm;

/// <summary>
/// Токены хостингов и секреты вебхуков под DataProtection (purpose «Flow.Scm»). Ключи — те же, что у cookie и
/// антифоржери (DataProtection:KeysPath, том auth-keys): эфемерные ключи после рестарта не расшифруют секрет, и
/// TryUnprotect вернёт null — подключение попросит ввести токен заново, а не упадёт.
/// </summary>
internal sealed class DataProtectionScmSecretProtector(IDataProtectionProvider provider) : IScmSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Flow.Scm");

    public string Protect(string secret) => _protector.Protect(secret);

    public string? TryUnprotect(string protectedSecret)
    {
        try
        {
            return _protector.Unprotect(protectedSecret);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
