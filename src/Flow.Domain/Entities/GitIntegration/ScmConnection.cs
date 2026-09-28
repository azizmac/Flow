namespace Flow.Domain.Entities.GitIntegration;

/// <summary>Подключение к хостингу: адрес (для self-hosted), токен (зашифрован), результат последней проверки.</summary>
public sealed class ScmConnection
{
    public const int NameMaxLength = 80;
    public const int UrlMaxLength = 500;
    public const int ErrorMaxLength = 500;

    public Guid Id { get; private set; }

    public ScmProvider Provider { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Адрес self-hosted экземпляра; для GitHub.com — null.</summary>
    public string? BaseUrl { get; private set; }

    public ScmAuthKind AuthKind { get; private set; }

    /// <summary>
    /// Токен (или закрытый ключ GitHub App в PEM), зашифрованный IDataProtector: в БД и в логах открытого текста нет.
    /// </summary>
    public string SecretProtected { get; private set; } = string.Empty;

    /// <summary>Id приложения GitHub App — издатель JWT.</summary>
    public long? AppId { get; private set; }

    /// <summary>Id установки приложения в организации или аккаунте — у неё берётся токен на час.</summary>
    public long? InstallationId { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastCheckAt { get; private set; }

    /// <summary>Логин, под которым токен прошёл проверку.</summary>
    public string? CheckedLogin { get; private set; }

    public string? LastError { get; private set; }

    private ScmConnection()
    {
        // EF Core
    }

    public static ScmConnection Create(ScmProvider provider, string name, string? baseUrl, string secretProtected, Guid createdById,
        ScmAuthKind authKind = ScmAuthKind.Token, long? appId = null, long? installationId = null)
    {
        if (!Enum.IsDefined(provider))
            throw new ArgumentException($"Unknown provider {provider}.", nameof(provider));
        if (createdById == Guid.Empty)
            throw new ArgumentException("Author id must not be empty.", nameof(createdById));
        if (provider != ScmProvider.GitHub && string.IsNullOrWhiteSpace(baseUrl))
            throw new ArgumentException("Для self-hosted хостинга нужен адрес.", nameof(baseUrl));

        if (!Enum.IsDefined(authKind))
            throw new ArgumentException($"Unknown auth kind {authKind}.", nameof(authKind));
        if (authKind == ScmAuthKind.GitHubApp && provider != ScmProvider.GitHub)
            throw new ArgumentException("GitHub App — только для GitHub.", nameof(authKind));

        var connection = new ScmConnection
        {
            Id = Guid.NewGuid(), Provider = provider, AuthKind = authKind, CreatedById = createdById, CreatedAt = DateTime.UtcNow
        };
        connection.Update(name, baseUrl, secretProtected);
        if (authKind == ScmAuthKind.GitHubApp)
            connection.SetApp(appId, installationId);
        return connection;
    }

    /// <summary>Id приложения и установки GitHub App. Смена сбрасывает результат проверки: токен выдаст уже другая установка.</summary>
    public void SetApp(long? appId, long? installationId)
    {
        if (AuthKind != ScmAuthKind.GitHubApp)
            throw new InvalidOperationException("Подключение входит по токену, а не как GitHub App.");
        if (appId is not > 0 || installationId is not > 0)
            throw new ArgumentException("Для GitHub App нужны Id приложения и Id установки — положительные числа.", nameof(appId));
        if (AppId == appId && InstallationId == installationId)
            return;
        AppId = appId;
        InstallationId = installationId;
        LastCheckAt = null;
        CheckedLogin = null;
        LastError = null;
    }

    /// <summary>Имя, адрес и (если передан) новый токен. Смена токена сбрасывает результат проверки.</summary>
    public void Update(string name, string? baseUrl, string? secretProtected)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
            throw new ArgumentException($"Название — от 1 до {NameMaxLength} символов.", nameof(name));
        BaseUrl = NormalizeUrl(baseUrl);
        Name = trimmed;
        if (secretProtected is not null)
        {
            if (secretProtected.Length == 0)
                throw new ArgumentException("Токен не может быть пустым.", nameof(secretProtected));
            SecretProtected = secretProtected;
            LastCheckAt = null;
            CheckedLogin = null;
            LastError = null;
        }
    }

    public void MarkChecked(string? login, string? error, DateTime utcNow)
    {
        LastCheckAt = utcNow;
        CheckedLogin = error is null ? login : null;
        LastError = error is null ? null : error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
    }

    /// <summary>Адрес без хвостового «/»: http(s) и ничего больше — иначе токен ушёл бы по file:// или на чужую схему.</summary>
    public static string? NormalizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var value = url.Trim().TrimEnd('/');
        if (value.Length > UrlMaxLength || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new ArgumentException("Адрес хостинга — http(s)-ссылка.", nameof(url));
        return value;
    }
}
