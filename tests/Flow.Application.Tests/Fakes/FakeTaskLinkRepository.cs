using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <summary>Связи в памяти; «заблокирована» считается по финальности статуса источника из досок фейка.</summary>
public sealed class FakeTaskLinkRepository(FakeBoardRepository boards, FakeTaskItemRepository tasks) : ITaskLinkRepository
{
    private readonly List<TaskLink> _links = [];

    public IReadOnlyList<TaskLink> All => _links;

    public Task<TaskLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_links.SingleOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<TaskLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskLink>>(_links.Where(l => l.SourceTaskId == taskId || l.TargetTaskId == taskId).ToList());

    public Task<bool> ExistsAsync(Guid sourceTaskId, Guid targetTaskId, TaskLinkType type, CancellationToken cancellationToken) =>
        Task.FromResult(_links.Any(l => l.SourceTaskId == sourceTaskId && l.TargetTaskId == targetTaskId && l.Type == type));

    public async Task<IReadOnlyDictionary<Guid, int>> CountBlockersAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        var finalStatuses = (await boards.GetAllAsync(cancellationToken)).SelectMany(b => b.Statuses).Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var result = new Dictionary<Guid, int>();
        foreach (var link in _links.Where(l => l.Type == TaskLinkType.Blocks && taskIds.Contains(l.TargetTaskId)))
        {
            var source = await tasks.GetByIdAsync(link.SourceTaskId, cancellationToken);
            if (source is not null && !finalStatuses.Contains(source.StatusId))
                result[link.TargetTaskId] = result.GetValueOrDefault(link.TargetTaskId) + 1;
        }

        return result;
    }

    public Task<IReadOnlyList<Guid>> GetBlockedTargetsAsync(IReadOnlyCollection<Guid> sourceTaskIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(_links
            .Where(l => l.Type == TaskLinkType.Blocks && sourceTaskIds.Contains(l.SourceTaskId))
            .Select(l => l.TargetTaskId).Distinct().ToList());

    public async Task<IReadOnlyList<(Guid SourceId, Guid TargetId)>> GetBlocksWithinBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var result = new List<(Guid, Guid)>();
        foreach (var link in _links.Where(l => l.Type == TaskLinkType.Blocks))
            if ((await tasks.GetByIdAsync(link.SourceTaskId, cancellationToken))?.BoardId == boardId
                && (await tasks.GetByIdAsync(link.TargetTaskId, cancellationToken))?.BoardId == boardId)
                result.Add((link.SourceTaskId, link.TargetTaskId));
        return result;
    }

    public void Add(TaskLink link) => _links.Add(link);

    public void Remove(TaskLink link) => _links.Remove(link);

    /// <summary>Каскад БД при удалении задачи — фейк повторяет его явно, когда тесту это важно.</summary>
    public void RemoveForTask(Guid taskId) => _links.RemoveAll(l => l.SourceTaskId == taskId || l.TargetTaskId == taskId);
}
