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

    public void Add(TaskActivity activity) => db.TaskActivities.Add(activity);
}
