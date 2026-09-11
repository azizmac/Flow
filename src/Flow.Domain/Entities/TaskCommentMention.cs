namespace Flow.Domain.Entities;

/// <summary>
/// Упомянутый в комментарии пользователь (<c>@username</c>). Owned-коллекция <see cref="TaskComment.Mentions"/>,
/// создаётся только внутри <see cref="TaskComment"/>; разбор текста — в Application (MentionParser).
/// </summary>
public sealed class TaskCommentMention
{
    public Guid CommentId { get; private set; }

    public Guid UserId { get; private set; }

    private TaskCommentMention()
    {
        // EF Core
    }

    internal TaskCommentMention(Guid commentId, Guid userId)
    {
        CommentId = commentId;
        UserId = userId;
    }
}
