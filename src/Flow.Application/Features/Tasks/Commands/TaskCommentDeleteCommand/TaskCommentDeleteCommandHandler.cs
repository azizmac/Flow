using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentDeleteCommand;

internal sealed class TaskCommentDeleteCommandHandler(
    ITaskCommentRepository comments,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    ITaskItemRepository tasks,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCommentDeleteCommand, bool>
{
    public async Task<bool> Handle(TaskCommentDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var comment = await comments.GetByIdAsync(request.CommentId, cancellationToken);
        if (comment is null)
            return false;

        // Права — в проекте задачи комментария; задачи нет (удалили вместе с комментариями) — как будто нет и его.
        var task = await tasks.GetByIdAsync(comment.TaskId, cancellationToken);
        if (task is null)
            return false;

        permissions.EnsureCanDeleteComment(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), comment);

        comments.Remove(comment);
        activities.Add(TaskActivity.CommentDeleted(comment.TaskId, actor.Id, comment.Id));
        searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, boardId: null, SearchIndexOperation.Delete);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
