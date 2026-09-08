using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Ids;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class TaskItemRepository(FlowDbContext db) : ITaskItemRepository
{
    public Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken) =>
        db.TaskItems.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(BoardId boardId, CancellationToken cancellationToken) =>
        await db.TaskItems.Where(t => t.BoardId == boardId).AsNoTracking().ToListAsync(cancellationToken);

    public Task<bool> StatusBelongsToBoardAsync(StatusId statusId, BoardId boardId, CancellationToken cancellationToken) =>
        db.Statuses.AnyAsync(s => s.Id == statusId && s.BoardId == boardId, cancellationToken);

    public void Add(TaskItem task) => db.TaskItems.Add(task);

    public void Remove(TaskItem task) => db.TaskItems.Remove(task);
}
