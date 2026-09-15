using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;

internal sealed class TaskCommentDeleteCommandHandler(
    ITaskCommentRepository comments,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCommentDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskCommentDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return false;

        permissions.EnsureCanDeleteComment(actor, comment);

        var task = await tasks.GetByIdAsync(comment.TaskId, cancellationToken);

        comments.Remove(comment);
        activities.Add(TaskActivity.CommentDeleted(comment.TaskId, actor.Id, comment.Id));
        searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, task?.BoardId, SearchIndexOperation.Delete);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
