using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;

/// <summary>Править может только автор. Записи в журнал нет — в ленте видно «изменено» по EditedAt.</summary>
public sealed record TaskCommentEditCommand(Guid ActorId, Guid CommentId, string Body) : IRequest<TaskCommentResult>;
