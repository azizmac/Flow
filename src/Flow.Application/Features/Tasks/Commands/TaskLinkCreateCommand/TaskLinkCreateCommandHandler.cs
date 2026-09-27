using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using MediatR;
using TaskLinkType = Flow.Domain.Entities.TaskLinkType;

namespace Flow.Application.Features.Tasks.Commands.TaskLinkCreateCommand;

internal sealed class TaskLinkCreateCommandHandler(
    ITaskItemRepository tasks,
    ITaskLinkRepository links,
    ITaskActivityRepository activities,
    IBoardRepository boards,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskLinkCreateCommand, TaskLinkCreateResult>
{
    /// <summary>Сколько задач обходим в поисках цикла: предупреждение — удобство, а не гарантия.</summary>
    private const int CycleSearchLimit = 500;

    public async Task<TaskLinkCreateResult> Handle(TaskLinkCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskLinkCreateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        if (request.TargetId is null && string.IsNullOrWhiteSpace(request.TargetCode))
            throw new ArgumentException("TargetId or TargetCode is required.", nameof(request.TargetId));

        var other = request.TargetId is { } targetId
            ? await tasks.GetByIdAsync(targetId, cancellationToken)
            : await tasks.GetByCodeAsync(request.TargetCode!, cancellationToken);

        // Задача скрытого проекта должна выглядеть так же, как несуществующая: код в запросе не подтверждает её.
        if (other is null || !(await projectAccess.GetAsync(actor, other.BoardId, cancellationToken)).CanView)
            throw new InvalidOperationException("The task to link is not found.");

        var (source, target) = request.Inward ? (other, task) : (task, other);
        var link = TaskLink.Create(source.Id, target.Id, request.Type, actor.Id);
        if (await links.ExistsAsync(link.SourceTaskId, link.TargetTaskId, link.Type, cancellationToken))
            return TaskLinkCreateResult.Duplicate();

        var cycle = link.Type == TaskLinkType.Blocks && await ClosesCycleAsync(link, cancellationToken);

        links.Add(link);
        activities.Add(TaskActivity.LinkAdded(link.SourceTaskId, actor.Id, link.Type, true, link.TargetTaskId));
        activities.Add(TaskActivity.LinkAdded(link.TargetTaskId, actor.Id, link.Type, false, link.SourceTaskId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var board = await boards.GetByIdAsync(other.BoardId, cancellationToken);
        var peer = new TaskLinkPeer(other.Id, false, other.Code.Value, other.Title, other.BoardId, other.StatusId,
            board?.Statuses.Any(s => s.Id == other.StatusId && s.IsFinal) == true);
        return TaskLinkCreateResult.Success(new TaskLinkCreatedResponse(TaskLinkResponses.Build(link, task.Id, peer), cycle));
    }

    /// <summary>
    /// «A блокирует B» замыкает цикл, если B уже (транзитивно) блокирует A. Цикл не запрещён — запрет проверял бы
    /// весь граф ради редкого случая, — но человеку стоит о нём знать. Обход в ширину с потолком.
    /// </summary>
    private async Task<bool> ClosesCycleAsync(TaskLink link, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid> { link.TargetTaskId };
        var frontier = new List<Guid> { link.TargetTaskId };
        while (frontier.Count > 0 && visited.Count < CycleSearchLimit)
        {
            var next = await links.GetBlockedTargetsAsync(frontier, cancellationToken);
            if (next.Contains(link.SourceTaskId))
                return true;

            frontier = next.Where(visited.Add).ToList();
        }

        return false;
    }
}
