using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class MilestoneRepository(FlowDbContext db) : IMilestoneRepository
{
    public Task<Milestone?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Milestones.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Milestone>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var milestones = await db.Milestones
            .Where(m => m.BoardId == boardId || EF.Property<List<Guid>>(m, "_sharedBoardIds").Contains(boardId))
            .ToListAsync(cancellationToken);
        // Свои вехи — перед общими, внутри — открытые по порядку, затем закрытые от недавних.
        return milestones
            .OrderBy(m => m.IsClosed)
            .ThenBy(m => m.BoardId != boardId)
            .ThenBy(m => m.IsClosed ? 0 : m.SortOrder)
            .ThenByDescending(m => m.ClosedAt)
            .ToList();
    }

    public async Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken) =>
        (await db.Milestones.Where(m => m.BoardId == boardId).MaxAsync(m => (int?)m.SortOrder, cancellationToken) ?? -1) + 1;

    public void Add(Milestone milestone) => db.Milestones.Add(milestone);

    public void Remove(Milestone milestone) => db.Milestones.Remove(milestone);
}
