using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class SprintRepository(FlowDbContext db) : ISprintRepository
{
    public Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sprints.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Sprint>> GetByBoardAsync(Guid boardId, bool includeCompleted, CancellationToken cancellationToken)
    {
        var sprints = await db.Sprints
            .Where(s => s.BoardId == boardId && (includeCompleted || s.State != SprintState.Completed))
            .ToListAsync(cancellationToken);

        return sprints
            .OrderBy(s => s.State switch { SprintState.Active => 0, SprintState.Planned => 1, _ => 2 })
            .ThenBy(s => s.State == SprintState.Completed ? 0 : s.SortOrder)
            .ThenByDescending(s => s.CompletedAt)
            .ToList();
    }

    public Task<bool> HasActiveAsync(Guid boardId, Guid? exceptId, CancellationToken cancellationToken) =>
        db.Sprints.AnyAsync(s => s.BoardId == boardId && s.State == SprintState.Active && s.Id != exceptId, cancellationToken);

    public async Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken) =>
        (await db.Sprints.Where(s => s.BoardId == boardId).MaxAsync(s => (int?)s.SortOrder, cancellationToken) ?? -1) + 1;

    public void Add(Sprint sprint) => db.Sprints.Add(sprint);

    public void Remove(Sprint sprint) => db.Sprints.Remove(sprint);
}
