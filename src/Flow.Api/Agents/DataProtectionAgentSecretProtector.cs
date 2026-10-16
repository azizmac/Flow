using System.Security.Cryptography;
using Flow.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Flow.Api.Agents;

/// <summary>
/// Ключи API провайдеров агента под DataProtection (purpose «Flow.Agent», отдельный от токенов Git). Ключи шифрования —
/// те же, что у cookie (DataProtection:KeysPath, том auth-keys): дамп базы без этого тома ключей не раскрывает, а после
/// восстановления без него TryUnprotect вернёт null — провайдер попросит ввести ключ заново.
/// </summary>
internal sealed class DataProtectionAgentSecretProtector(IDataProtectionProvider provider) : IAgentSecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("Flow.Agent");

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
