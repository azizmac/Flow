namespace Flow.Domain.Entities;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A). Forgejo = Gitea по протоколу: один разбор,
// различие — заголовки подписи и доставки. Секреты хранятся зашифрованными (IDataProtector, purpose Flow.Scm):
// домен видит только непрозрачную строку.

public enum ScmProvider
{
    GitHub = 0,
    GitLab = 1,
    Gitea = 2,
    Forgejo = 3
}

/// <summary>Чем подключение входит в API: личный/сервисный токен или GitHub App (закрытый ключ + установка, этап 5B).</summary>
public enum ScmAuthKind
{
    Token = 0,
    GitHubApp = 1
}

public enum ScmLinkKind
{
    Branch = 0,
    Commit = 1,
    PullRequest = 2
}

public enum ScmLinkState
{
    Open = 0,
    Draft = 1,
    Merged = 2,
    Closed = 3
}

public enum ScmDeliveryStatus
{
    Pending = 0,
    Done = 1,
    Failed = 2,
    Ignored = 3
}

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

/// <summary>Репозиторий подключения, с которого Flow принимает вебхуки. Секрет вебхука зашифрован.</summary>
public sealed class ScmRepository
{
    public const int NameMaxLength = 300;

    public Guid Id { get; private set; }

    public Guid ConnectionId { get; private set; }

    /// <summary>Id у хостинга: у GitHub/Gitea — число, у GitLab — id проекта. Строкой.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    /// <summary>org/repo.</summary>
    public string FullName { get; private set; } = string.Empty;

    public string WebUrl { get; private set; } = string.Empty;

    public string DefaultBranch { get; private set; } = "main";

    /// <summary>Id вебхука у хостинга; null — создать не удалось, настроен вручную.</summary>
    public string? WebhookId { get; private set; }

    public string WebhookSecretProtected { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastDeliveryAt { get; private set; }

    private ScmRepository()
    {
        // EF Core
    }

    public static ScmRepository Create(Guid connectionId, string externalId, string fullName, string webUrl, string? defaultBranch, string webhookSecretProtected)
    {
        if (connectionId == Guid.Empty)
            throw new ArgumentException("Connection id must not be empty.", nameof(connectionId));
        if (string.IsNullOrWhiteSpace(externalId) || string.IsNullOrWhiteSpace(fullName) || fullName.Length > NameMaxLength)
            throw new ArgumentException("Репозиторий без id или имени.", nameof(fullName));
        if (string.IsNullOrWhiteSpace(webhookSecretProtected))
            throw new ArgumentException("Нужен секрет вебхука.", nameof(webhookSecretProtected));

        return new ScmRepository
        {
            Id = Guid.NewGuid(),
            ConnectionId = connectionId,
            ExternalId = externalId.Trim(),
            FullName = fullName.Trim(),
            WebUrl = ScmConnection.NormalizeUrl(webUrl) ?? throw new ArgumentException("Нет адреса репозитория.", nameof(webUrl)),
            DefaultBranch = string.IsNullOrWhiteSpace(defaultBranch) ? "main" : defaultBranch.Trim(),
            WebhookSecretProtected = webhookSecretProtected,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetWebhook(string? webhookId) => WebhookId = string.IsNullOrWhiteSpace(webhookId) ? null : webhookId;

    public void SetDefaultBranch(string? branch)
    {
        if (!string.IsNullOrWhiteSpace(branch))
            DefaultBranch = branch.Trim();
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Повторное подключение: новый секрет вебхука, приём включён. Связи задач прежнего подключения остаются.</summary>
    public void Reactivate(string webhookSecretProtected)
    {
        if (string.IsNullOrWhiteSpace(webhookSecretProtected))
            throw new ArgumentException("Нужен секрет вебхука.", nameof(webhookSecretProtected));
        WebhookSecretProtected = webhookSecretProtected;
        WebhookId = null;
        IsActive = true;
    }

    public void MarkDelivery(DateTime utcNow) => LastDeliveryAt = utcNow;
}

/// <summary>
/// Привязка репозитория к проекту: коды задач связываются только в привязанных проектах — иначе публичный
/// репозиторий с «fix WEB-12» прицепил бы чужой текст к задаче приватного проекта. Монорепозиторий — несколько привязок.
/// </summary>
public sealed class ScmRepositoryBoard
{
    public Guid RepositoryId { get; private set; }

    public Guid BoardId { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private ScmRepositoryBoard()
    {
        // EF Core
    }

    /// <summary>Автопереход (этап 5C): PR открыт → этот статус проекта; null — выключен.</summary>
    public Guid? OnPullRequestOpenedStatusId { get; private set; }

    /// <summary>Автопереход: PR влит в ветку по умолчанию → этот статус; null — выключен.</summary>
    public Guid? OnPullRequestMergedStatusId { get; private set; }

    /// <summary>Смарт-коммиты (#done, #comment, #status) в push'ах в ветку по умолчанию.</summary>
    public bool SmartCommits { get; private set; }

    public static ScmRepositoryBoard Create(Guid repositoryId, Guid boardId, Guid createdById) =>
        new() { RepositoryId = repositoryId, BoardId = boardId, CreatedById = createdById, CreatedAt = DateTime.UtcNow };

    /// <summary>Настройки автоматизации привязки. Принадлежность статусов проекту проверяет хендлер — он видит доску.</summary>
    public void Configure(Guid? onPullRequestOpenedStatusId, Guid? onPullRequestMergedStatusId, bool smartCommits)
    {
        OnPullRequestOpenedStatusId = onPullRequestOpenedStatusId;
        OnPullRequestMergedStatusId = onPullRequestMergedStatusId;
        SmartCommits = smartCommits;
    }
}

/// <summary>
/// Ветка, коммит или PR, связанные с задачей. Уникальна по (задача, репозиторий, вид, внешний id): повтор
/// доставки обновляет, а не плодит. Заголовки и сообщения — недоверенный текст: показываются как текст.
/// </summary>
public sealed class ScmLink
{
    public const int TitleMaxLength = 500;
    public const int ExternalIdMaxLength = 255;

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    public Guid RepositoryId { get; private set; }

    public ScmLinkKind Kind { get; private set; }

    /// <summary>sha коммита, номер PR или имя ветки.</summary>
    public string ExternalId { get; private set; } = string.Empty;

    public string Url { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public ScmLinkState? State { get; private set; }

    public string? AuthorLogin { get; private set; }

    public Guid? AuthorUserId { get; private set; }

    public string? SourceBranch { get; private set; }

    public string? TargetBranch { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>
    /// Что Flow не сделал по этой связи и почему (этап 5C): «автопереход не разрешён workflow: …», «смарт-коммит не
    /// выполнен: …». Показывается в блоке «Разработка»; null — пометок нет.
    /// </summary>
    public string? Note { get; private set; }

    public const int NoteMaxLength = 500;

    private ScmLink()
    {
        // EF Core
    }

    public static ScmLink Create(Guid taskId, Guid repositoryId, ScmLinkKind kind, string externalId) =>
        new()
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            RepositoryId = repositoryId,
            Kind = kind,
            ExternalId = Trim(externalId, ExternalIdMaxLength),
            OccurredAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    /// <summary>Свежие данные события. OccurredAt — время события у хостинга, не приёма.</summary>
    public void Apply(string url, string title, ScmLinkState? state, string? authorLogin, Guid? authorUserId,
        string? sourceBranch, string? targetBranch, DateTime occurredAt)
    {
        Url = Trim(url, 1000);
        Title = Trim(title, TitleMaxLength);
        State = state;
        AuthorLogin = authorLogin is null ? null : Trim(authorLogin, 100);
        AuthorUserId = authorUserId ?? AuthorUserId;
        SourceBranch = sourceBranch is null ? null : Trim(sourceBranch, ExternalIdMaxLength);
        TargetBranch = targetBranch is null ? null : Trim(targetBranch, ExternalIdMaxLength);
        OccurredAt = DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc);
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetNote(string? note) =>
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Length <= NoteMaxLength ? note : note[..NoteMaxLength];

    public void SetState(ScmLinkState state)
    {
        State = state;
        UpdatedAt = DateTime.UtcNow;
    }

    private static string Trim(string value, int max)
    {
        var v = value?.Trim() ?? string.Empty;
        return v.Length <= max ? v : v[..max];
    }
}

/// <summary>
/// Доставка вебхука: принята, подпись сошлась, событие уже нормализовано до нужных Flow полей (Payload — JSON
/// нормализованного события, а не исходный мегабайтный push). Обрабатывает воркер, повтор с backoff до MaxAttempts.
/// </summary>
public sealed class ScmDelivery
{
    /// <summary>
    /// Не вебхук, а задание дозагрузки истории (этап 5B): последние PR и коммиты ветки по умолчанию через API. Живёт в
    /// той же очереди — тот же воркер, повторы, диагностика и срок хранения.
    /// </summary>
    public const string BackfillEvent = "flow:backfill";

    public const int MaxAttempts = 8;
    public const int ErrorMaxLength = 1000;
    public const int DeliveryIdMaxLength = 100;

    public Guid Id { get; private set; }

    public Guid RepositoryId { get; private set; }

    public string DeliveryId { get; private set; } = string.Empty;

    public string Event { get; private set; } = string.Empty;

    public DateTime ReceivedAt { get; private set; }

    public ScmDeliveryStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTime NextAttemptAt { get; private set; }

    public string? LastError { get; private set; }

    public string Payload { get; private set; } = "{}";

    private ScmDelivery()
    {
        // EF Core
    }

    public static ScmDelivery Create(Guid repositoryId, string deliveryId, string eventName, string? payload)
    {
        var now = DateTime.UtcNow;
        var id = string.IsNullOrWhiteSpace(deliveryId) ? Guid.NewGuid().ToString() : deliveryId.Trim();
        return new ScmDelivery
        {
            Id = Guid.NewGuid(),
            RepositoryId = repositoryId,
            DeliveryId = id.Length <= DeliveryIdMaxLength ? id : id[..DeliveryIdMaxLength],
            Event = eventName.Length <= 60 ? eventName : eventName[..60],
            ReceivedAt = now,
            NextAttemptAt = now,
            Status = payload is null ? ScmDeliveryStatus.Ignored : ScmDeliveryStatus.Pending,
            Payload = payload ?? "{}"
        };
    }

    public bool IsBackfill => Event == BackfillEvent;

    /// <summary>Задание дозагрузки истории репозитория — Pending сразу.</summary>
    public static ScmDelivery CreateBackfill(Guid repositoryId) =>
        Create(repositoryId, $"backfill-{Guid.NewGuid():N}", BackfillEvent, "{}");

    /// <summary>
    /// Отложить без траты попытки: хостинг исчерпал лимит запросов — это не сбой, а пауза до сброса лимита.
    /// </summary>
    public void Postpone(DateTime until, string reason)
    {
        if (Status != ScmDeliveryStatus.Pending)
            throw new InvalidOperationException("Отложить можно только доставку в очереди.");
        NextAttemptAt = DateTime.SpecifyKind(until, DateTimeKind.Utc);
        LastError = reason.Length <= ErrorMaxLength ? reason : reason[..ErrorMaxLength];
    }

    /// <summary>Повтор вручную (диагностика доставок): Failed снова в очередь, попытки с нуля.</summary>
    public void Retry(DateTime utcNow)
    {
        if (Status != ScmDeliveryStatus.Failed)
            throw new InvalidOperationException("Повторить можно только доставку с ошибкой.");
        Status = ScmDeliveryStatus.Pending;
        Attempts = 0;
        NextAttemptAt = utcNow;
        LastError = null;
    }

    public void MarkDone()
    {
        Status = ScmDeliveryStatus.Done;
        Attempts++;
        LastError = null;
    }

    /// <summary>Сбой обработки: повтор с backoff 5 с → 5 мин, после MaxAttempts — Failed.</summary>
    public void MarkFailed(string error, DateTime utcNow)
    {
        Attempts++;
        LastError = error.Length <= ErrorMaxLength ? error : error[..ErrorMaxLength];
        if (Attempts >= MaxAttempts)
        {
            Status = ScmDeliveryStatus.Failed;
            return;
        }

        var delay = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Attempts - 1)));
        NextAttemptAt = utcNow + delay;
    }
}

/// <summary>
/// Профиль бота интеграции (docs/TZ_scm_integration.md, принятые решения): от его имени пишутся автопереходы, когда
/// автор PR не сопоставлен или не может править задачу. Учётной записи в Auth-модуле нет — войти им нельзя; профиль
/// сразу деактивирован, поэтому его не назначить исполнителем и не упомянуть. Журнал ссылается на него FK — профиль
/// создаётся при первой надобности.
/// </summary>
public static class ScmBot
{
    public static readonly Guid Id = new("00000000-0000-0000-0000-00000000f10b");

    public const string Username = "flow-bot";

    public static User Create()
    {
        var bot = User.CreateWithId(Id, Username, "flow-bot@flow.local", "Flow", "Bot");
        bot.Deactivate();
        return bot;
    }
}
