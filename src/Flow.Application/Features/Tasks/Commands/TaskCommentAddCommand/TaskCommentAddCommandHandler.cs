using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Mentions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCommentAddCommand;

internal sealed class TaskCommentAddCommandHandler(
    ITaskItemRepository tasks,
    ITaskCommentRepository comments,
    ITaskActivityRepository activities,
    MentionResolver mentions,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskCommentAddCommand, TaskCommentResult>
{
    public async Task<TaskCommentResult> Handle(TaskCommentAddCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskCommentResult.NotFound();

        permissions.EnsureCanComment(actor);

        var mentioned = await mentions.ResolveAsync(request.Body, cancellationToken);
        var comment = TaskComment.Create(task.Id, actor.Id, request.Body, mentioned);

        comments.Add(comment);
        activities.Add(TaskActivity.CommentAdded(task.Id, actor.Id, comment.Id));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TaskCommentResult.Success(comment.ToResponse());
    }
}
