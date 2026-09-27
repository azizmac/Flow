using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskTreeQuery;

internal sealed class TaskTreeQueryHandler(IBoardRepository boards, ITaskItemRepository tasks, ITaskCommentRepository comments, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<TaskTreeQuery, IReadOnlyList<TaskTreeNode>?>
{
    public async Task<IReadOnlyList<TaskTreeNode>?> Handle(TaskTreeQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        if (!(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken)).CanView)
            return null;
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;

        if (request.RootId is { } rootId && (await tasks.GetByIdAsync(rootId, cancellationToken))?.BoardId != request.BoardId)
            return null;

        var entries = await tasks.GetTreeAsync(request.BoardId, request.RootId, cancellationToken);

        // Счётчики — одним GROUP BY на всё дерево, как у списка.
        var ids = entries.Select(e => e.Task.Id).ToList();
        var commentCounts = await comments.CountByTaskIdsAsync(ids, cancellationToken);
        var children = await tasks.CountChildrenAsync(ids, cancellationToken);

        return entries
            .Select(e => new TaskTreeNode(e.Task.ToResponse(commentCounts.GetValueOrDefault(e.Task.Id), children.GetValueOrDefault(e.Task.Id)), e.Depth))
            .ToList();
    }
}
