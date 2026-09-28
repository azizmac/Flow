namespace Flow.Domain.Entities.GitIntegration;

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
