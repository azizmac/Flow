using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

/// <summary>Mentions — owned-коллекция, EF подгружает её автоматически, явный Include не нужен.</summary>
public sealed class TaskCommentRepository(FlowDbContext db) : ITaskCommentRepository
{
    public Task<TaskComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.TaskComments.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskComment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskComments
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountByTaskIdsAsync(
        IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
            return new Dictionary<Guid, int>();

        return await db.TaskComments
            .Where(c => taskIds.Contains(c.TaskId))
            .GroupBy(c => c.TaskId)
            .Select(g => new { TaskId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TaskId, x => x.Count, cancellationToken);
    }

    public void Add(TaskComment comment) => db.TaskComments.Add(comment);

    public void Remove(TaskComment comment) => db.TaskComments.Remove(comment);
}
