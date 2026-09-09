using Flow.Shared.Contracts.Users;

namespace Flow.Client.Services;

/// <summary>Подписи и порядок типов внешних ссылок (UserLinkType) для карточки и профиля.</summary>
public static class UserLinks
{
    public static string Label(UserLinkType type) => type switch
    {
        UserLinkType.GitHub => "GitHub",
        UserLinkType.GitLab => "GitLab",
        UserLinkType.Telegram => "Telegram",
        UserLinkType.LinkedIn => "LinkedIn",
        UserLinkType.Website => "Сайт",
        _ => "Другое"
    };

    public static readonly UserLinkType[] Order =
    [
        UserLinkType.GitHub, UserLinkType.GitLab, UserLinkType.Telegram,
        UserLinkType.LinkedIn, UserLinkType.Website, UserLinkType.Other
    ];

    /// <summary>«github.com/ilya» вместо полного URL — для компактных карточек.</summary>
    public static string Short(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u))
            return url;

        var host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
        var path = u.AbsolutePath.TrimEnd('/');
        return path.Length > 1 ? host + path : host;
    }
}
