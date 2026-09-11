namespace Flow.Domain.Entities;

/// <summary>
/// Комментарий к задаче в Markdown. Отдельная сущность, а не тип активности: у него есть автор, тело,
/// правка (<see cref="EditedAt"/>) и упоминания. Кто вправе писать/править/удалять — решает Application
/// (IPermissionService). Удаляется физически; след остаётся в журнале (TaskActivityType.CommentDeleted).
/// </summary>
public sealed class TaskComment
{
    public const int BodyMaxLength = 10_000;

    private readonly List<TaskCommentMention> _mentions = [];

    public Guid Id { get; private set; }

    public Guid TaskId { get; private set; }

    /// <summary>Автор (User.Id).</summary>
    public Guid AuthorId { get; private set; }

    /// <summary>Markdown, 1..<see cref="BodyMaxLength"/> символов, без ведущих/замыкающих пробелов.</summary>
    public string Body { get; private set; } = string.Empty;

    public DateTime CreatedAt { get; private set; }

    /// <summary>Когда правили последний раз; null — ни разу.</summary>
    public DateTime? EditedAt { get; private set; }

    /// <summary>Упомянутые пользователи, без дублей.</summary>
    public IReadOnlyCollection<TaskCommentMention> Mentions => _mentions;

    private TaskComment()
    {
        // EF Core
    }

    private TaskComment(Guid taskId, Guid authorId, string body, IEnumerable<Guid> mentions)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("Task id must not be empty.", nameof(taskId));
        if (authorId == Guid.Empty)
            throw new ArgumentException("Author id must not be empty.", nameof(authorId));

        Id = Guid.NewGuid();
        TaskId = taskId;
        AuthorId = authorId;
        Body = ValidateBody(body);
        CreatedAt = DateTime.UtcNow;
        ReplaceMentions(mentions);
    }

    /// <summary>Единственная точка создания. <paramref name="mentions"/> — уже разрешённые UserId.</summary>
    public static TaskComment Create(Guid taskId, Guid authorId, string body, IEnumerable<Guid> mentions) =>
        new(taskId, authorId, body, mentions);

    /// <summary>
    /// Меняет текст и упоминания, ставит <see cref="EditedAt"/>. Если текст не изменился — ничего не делает
    /// (в ленте не появится ложное «изменено»). Возвращает, было ли изменение.
    /// </summary>
    public bool Edit(string body, IEnumerable<Guid> mentions)
    {
        var next = ValidateBody(body);
        if (next == Body)
            return false;

        Body = next;
        ReplaceMentions(mentions);
        EditedAt = DateTime.UtcNow;
        return true;
    }

    private void ReplaceMentions(IEnumerable<Guid> mentions)
    {
        _mentions.Clear();
        foreach (var userId in mentions.Distinct())
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("Mentioned user id must not be empty.", nameof(mentions));

            _mentions.Add(new TaskCommentMention(Id, userId));
        }
    }

    private static string ValidateBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            throw new ArgumentException("Comment body must not be empty.", nameof(body));

        var trimmed = body.Trim();
        if (trimmed.Length > BodyMaxLength)
            throw new ArgumentException($"Comment body must be at most {BodyMaxLength} characters.", nameof(body));

        return trimmed;
    }
}
