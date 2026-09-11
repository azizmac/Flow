using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;

/// <summary>Автор или Admin+. Комментарий удаляется физически, в журнале остаётся CommentDeleted. false — не найден.</summary>
public sealed record TaskCommentDeleteCommand(Guid ActorId, Guid CommentId) : IRequest<bool>;
