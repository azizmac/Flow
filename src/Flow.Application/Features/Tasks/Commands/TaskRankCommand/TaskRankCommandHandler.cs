using Flow.Application.Abstractions;
using Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Ranking;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskRankCommand;

internal sealed class TaskRankCommandHandler(
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<TaskRankCommand, TaskUpdateResult>
{
    public async Task<TaskUpdateResult> Handle(TaskRankCommand request, CancellationToken cancellationToken)
    {
        if (request.AfterId is null && request.BeforeId is null)
            throw new ArgumentException("Either AfterId or BeforeId is required.", nameof(request.AfterId));
        if (request.AfterId == request.TaskId || request.BeforeId == request.TaskId)
            throw new ArgumentException("A task cannot be its own neighbour.", nameof(request.AfterId));

        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null)
            return TaskUpdateResult.NotFound();

        permissions.EnsureCanEditTask(actor, await projectAccess.GetAsync(actor, task.BoardId, cancellationToken), task);

        var after = await NeighbourAsync(request.AfterId, task, cancellationToken);
        var before = await NeighbourAsync(request.BeforeId, task, cancellationToken);

        async Task<string> ComputeAsync()
        {
            // Соседа с одной стороны прислал клиент, второго берём из БД: клиент видит отфильтрованный список,
            // а место в проекте должно встать ровно между двумя соседними ключами, иначе Between откажет.
            var a = after?.Rank ?? (before is null ? null : await tasks.GetNeighborRankAsync(task.BoardId, before.Rank, after: false, task.Id, cancellationToken));
            var b = before?.Rank ?? (after is null ? null : await tasks.GetNeighborRankAsync(task.BoardId, after.Rank, after: true, task.Id, cancellationToken));
            if (a is not null && b is not null && string.CompareOrdinal(a, b) >= 0)
                throw new InvalidOperationException("AfterId must be ranked above BeforeId.");

            return FractionalIndex.Between(a, b);
        }

        task.SetRank(await ComputeAsync());
        await TaskRanks.SaveAsync(unitOfWork, async () => task.SetRank(await ComputeAsync()), cancellationToken);

        return TaskUpdateResult.Success(task.ToResponse());
    }

    private async Task<TaskItem?> NeighbourAsync(Guid? id, TaskItem task, CancellationToken cancellationToken)
    {
        if (id is null)
            return null;

        var neighbour = await tasks.GetByIdAsync(id.Value, cancellationToken);
        if (neighbour is null || neighbour.BoardId != task.BoardId)
            throw new InvalidOperationException($"Task {id} is not found in the task's project.");

        return neighbour;
    }
}
