namespace Flow.Domain.Entities;

/// <summary>
/// Внешняя ссылка в профиле пользователя (GitHub, Telegram, сайт...). Создаётся только через
/// <see cref="User.SetLink"/>, чтобы инвариант «не более одной ссылки каждого типа» проверялся в одном месте.
/// </summary>
public sealed class UserLink
{
    public const int MaxUrlLength = 500;

    public Guid UserId { get; private set; }

    public UserLinkType Type { get; private set; }

    public string Url { get; private set; } = string.Empty;

    private UserLink()
    {
        // EF Core
    }

    internal UserLink(Guid userId, UserLinkType type, string url)
    {
        UserId = userId;
        Type = type;
        Url = ValidateUrl(url);
    }

    internal void ChangeUrl(string url) => Url = ValidateUrl(url);

    /// <summary>Проверяет, что строка — абсолютный http/https URL. Переиспользуется для аватара.</summary>
    internal static string ValidateUrl(string url, string paramName = "url")
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Url must not be empty.", paramName);

        var trimmed = url.Trim();
        if (trimmed.Length > MaxUrlLength)
            throw new ArgumentException($"Url must be at most {MaxUrlLength} characters.", paramName);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("Url must be an absolute http or https URL.", paramName);

        return trimmed;
    }
}
