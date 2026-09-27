using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class SavedFilterRepository(FlowDbContext db) : ISavedFilterRepository
{
    public Task<SavedFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.SavedFilters.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SavedFilter>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.SavedFilters
            .Where(f => f.OwnerId == userId || f.Visibility == SavedFilterVisibility.Shared)
            .OrderBy(f => f.Name)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetStarredIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        (await db.SavedFilterStars.Where(s => s.UserId == userId).Select(s => s.FilterId).ToListAsync(cancellationToken)).ToHashSet();

    public Task<SavedFilterStar?> GetStarAsync(Guid filterId, Guid userId, CancellationToken cancellationToken) =>
        db.SavedFilterStars.FirstOrDefaultAsync(s => s.FilterId == filterId && s.UserId == userId, cancellationToken);

    public void Add(SavedFilter filter) => db.SavedFilters.Add(filter);

    public void Remove(SavedFilter filter) => db.SavedFilters.Remove(filter);

    public void AddStar(SavedFilterStar star) => db.SavedFilterStars.Add(star);

    public void RemoveStar(SavedFilterStar star) => db.SavedFilterStars.Remove(star);
}
