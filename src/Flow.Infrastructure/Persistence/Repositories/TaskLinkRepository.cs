using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskLinkRepository(FlowDbContext db) : ITaskLinkRepository
{
    public Task<TaskLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaskLinks.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskLinks
            .Where(l => l.SourceTaskId == taskId || l.TargetTaskId == taskId)
            .OrderBy(l => l.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid sourceTaskId, Guid targetTaskId, TaskLinkType type, CancellationToken cancellationToken) =>
        db.TaskLinks.AnyAsync(l => l.SourceTaskId == sourceTaskId && l.TargetTaskId == targetTaskId && l.Type == type, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountBlockersAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, int>();

        // Блокирует только незакрытая задача: финальность — признак статуса источника, поэтому два join'а.
        return await db.TaskLinks
            .Where(l => l.Type == TaskLinkType.Blocks && taskIds.Contains(l.TargetTaskId))
            .Join(db.TaskItems, l => l.SourceTaskId, t => t.Id, (l, t) => new { l.TargetTaskId, t.StatusId })
            .Join(db.Statuses, x => x.StatusId, s => s.Id, (x, s) => new { x.TargetTaskId, s.IsFinal })
            .Where(x => !x.IsFinal)
            .GroupBy(x => x.TargetTaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetBlockedTargetsAsync(IReadOnlyCollection<Guid> sourceTaskIds, CancellationToken cancellationToken) =>
        await db.TaskLinks
            .Where(l => l.Type == TaskLinkType.Blocks && sourceTaskIds.Contains(l.SourceTaskId))
            .Select(l => l.TargetTaskId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(TaskLink link) => db.TaskLinks.Add(link);

    public void Remove(TaskLink link) => db.TaskLinks.Remove(link);
}
