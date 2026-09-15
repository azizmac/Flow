using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;
using Flow.Application.Features.Tasks.Mentions;
using Flow.Application.Security;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentEditCommand;

internal sealed class TaskCommentEditCommandHandler(
    ITaskCommentRepository comments,
    ITaskItemRepository tasks,
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

        // Домен сам говорит, изменилось ли тело: то же самое тело — ни SaveChanges, ни переиндексации.
        if (comment.Edit(request.Body, mentioned))
        {
            var task = await tasks.GetByIdAsync(comment.TaskId, cancellationToken);
            searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, task?.BoardId, SearchIndexOperation.Upsert);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return TaskCommentResult.Success(comment.ToResponse());
    }
}
