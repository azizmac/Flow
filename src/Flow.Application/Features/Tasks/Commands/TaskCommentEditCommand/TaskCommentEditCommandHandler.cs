using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Mentions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;

internal sealed class TaskCommentEditCommandHandler(
    ITaskCommentRepository comments,
    MentionResolver mentions,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCommentEditCommand, TaskCommentResult>
{
    public async Task<TaskCommentResult> Handle(TaskCommentEditCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return TaskCommentResult.NotFound();

        permissions.EnsureCanEditComment(actor, comment);

        var mentioned = await mentions.ResolveAsync(request.Body, cancellationToken);

        // Правка тем же телом — no-op и в домене, и в индексе: пересчитывать вектор незачем.
        // BoardId здесь неизвестен и не нужен: чанки комментария воркер найдёт по его Id.
        if (comment.Edit(request.Body, mentioned))
        {
            searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, boardId: null, SearchIndexOperation.Upsert);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TaskCommentResult.Success(comment.ToResponse());
    }
}
