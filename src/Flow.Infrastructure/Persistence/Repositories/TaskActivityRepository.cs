using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskActivityRepository(FlowDbContext db) : ITaskActivityRepository
{
    public async Task<IReadOnlyList<TaskActivity>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskActivities
            .Where(a => a.TaskId == taskId)
            .OrderBy(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TaskActivity>> GetByTaskIdsAsync(
        IReadOnlyCollection<Guid> taskIds, IReadOnlyCollection<TaskActivityType> types, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return [];

        return await db.TaskActivities
            .Where(a => taskIds.Contains(a.TaskId) && types.Contains(a.Type))
            .OrderBy(a => a.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetTaskIdsWithValueAsync(TaskActivityType type, string value, CancellationToken cancellationToken) =>
        await db.TaskActivities
            .Where(a => a.Type == type && (a.OldValue == value || a.NewValue == value))
            .Select(a => a.TaskId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void Add(TaskActivity activity) => db.TaskActivities.Add(activity);
}
