using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;

/// <summary>Body — Markdown; @username разбирает сервер. Бросает ArgumentException при пустом теле.</summary>
public sealed record TaskCommentAddCommand(Guid ActorId, Guid TaskId, string Body) : IRequest<TaskCommentResult>;
