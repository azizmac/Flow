namespace Flow.Shared.Contracts.Tasks;

/// <summary>Body — Markdown как ввёл автор; Mentions — Id упомянутых через @username пользователей; EditedAt — null, если не правили.</summary>
public sealed record TaskCommentResponse(
    Guid Id,
    Guid TaskId,
    Guid AuthorId,
    string Body,
    IReadOnlyList<Guid> Mentions,
    DateTime CreatedAt,
    DateTime? EditedAt);
