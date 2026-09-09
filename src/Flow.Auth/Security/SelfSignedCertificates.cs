using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Flow.Auth.Options;

namespace Flow.Auth.Security;

/// <summary>
/// Загружает PFX по пути из конфигурации, а если файла нет — создаёт самоподписанный RSA-2048 на 2 года и сохраняет
/// его туда же (Docker: volume auth-certs, «одна кнопка» без ручного openssl). Для продакшена за пределами compose
/// сюда кладут настоящие сертификаты. Пароль PFX — из той же секции.
/// </summary>
public static class SelfSignedCertificates
{
    public static X509Certificate2 LoadOrCreate(AuthOptions.CertificateOptions options, string section, string subject, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
            throw new InvalidOperationException($"{section}:Path is required outside Development (PFX with the private key).");

        if (File.Exists(options.Path))
            return X509CertificateLoader.LoadPkcs12FromFile(options.Path, options.Password, X509KeyStorageFlags.EphemeralKeySet);

        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest($"CN={subject}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: false));

        var notBefore = DateTimeOffset.UtcNow.AddMinutes(-5);
        using var certificate = request.CreateSelfSigned(notBefore, notBefore.AddYears(2));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.Path))!);
        File.WriteAllBytes(options.Path, certificate.Export(X509ContentType.Pkcs12, options.Password));
        logger.LogWarning("{Section}: certificate not found, generated a self-signed one at {Path} (valid until {Until:d})",
            section, options.Path, notBefore.AddYears(2));

        return X509CertificateLoader.LoadPkcs12FromFile(options.Path, options.Password, X509KeyStorageFlags.EphemeralKeySet);
    }
}
