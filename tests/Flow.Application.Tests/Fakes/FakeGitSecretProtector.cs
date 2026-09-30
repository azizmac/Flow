using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

/// <summary>Обратимое «шифрование» для тестов: префикс, а не криптография.</summary>
public sealed class FakeGitSecretProtector : IGitSecretProtector
{
    public string Protect(string secret) => "p:" + secret;

    public string? TryUnprotect(string protectedSecret) => protectedSecret.StartsWith("p:") ? protectedSecret[2..] : null;
}
