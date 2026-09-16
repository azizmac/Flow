namespace Flow.Infrastructure.Storage;

/// <summary>
/// Секция "S3" — она уже прокидывается в контейнер api (docker-compose.yml), просто до сих пор
/// никем не читалась.
/// </summary>
public sealed class S3Options
{
    public const string SectionName = "S3";

    /// <summary>Адрес сервиса: внутри compose это http://s3:9000, снаружи — managed-хранилище.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = "flow";

    public bool UseSsl { get; set; }
}
