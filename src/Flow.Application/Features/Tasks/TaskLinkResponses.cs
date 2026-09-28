using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Features.Tasks;

/// <summary>
/// Связи глазами одной задачи (docs/TZ_task_model.md §5): направление относительно неё и краткая карточка второй
/// задачи. Вторая задача в проекте, которого actor не видит, — только Id с Restricted: связь создал тот, кто видел
/// обе, но название и код скрытой задачи раскрывать нельзя.
/// </summary>
internal sealed class TaskLinkResponses(ITaskItemRepository tasks, IBoardRepository boards, IProjectAccess projectAccess)
{
    public async Task<IReadOnlyList<TaskLinkResponse>> BuildAsync(User actor, Guid taskId, IReadOnlyList<TaskLink> links, CancellationToken cancellationToken)
    {
        var peers = new Dictionary<Guid, TaskLinkPeer>();
        var boardCache = new Dictionary<Guid, (bool CanView, Board? Board)>();

        foreach (var otherId in links.Select(l => l.OtherTaskId(taskId)).Distinct())
        {
            var other = await tasks.GetByIdAsync(otherId, cancellationToken);
            if (other is null)
            {
                peers[otherId] = new TaskLinkPeer(otherId, Restricted: true);
                continue;
            }

            if (!boardCache.TryGetValue(other.BoardId, out var board))
            {
                var canView = (await projectAccess.GetAsync(actor, other.BoardId, cancellationToken)).CanView;
                board = (canView, canView ? await boards.GetByIdAsync(other.BoardId, cancellationToken) : null);
                boardCache[other.BoardId] = board;
            }

            peers[otherId] = board.CanView
                ? new TaskLinkPeer(other.Id, false, other.Code.Value, other.Title, other.BoardId, other.StatusId,
                    board.Board?.Statuses.Any(s => s.Id == other.StatusId && s.IsFinal) == true)
                : new TaskLinkPeer(other.Id, Restricted: true);
        }

        return links.Select(l => Build(l, taskId, peers[l.OtherTaskId(taskId)])).ToList();
    }

    public static TaskLinkResponse Build(TaskLink link, Guid taskId, TaskLinkPeer other) => new(
        link.Id,
        link.Type.ToResponseLinkType(),
        link.Type == Domain.Entities.TaskLinkType.RelatesTo || link.IsOutwardFor(taskId),
        other,
        link.CreatedById,
        link.CreatedAt);
}
