using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class DashboardRepository(FlowDbContext db) : IDashboardRepository
{
    public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Dashboards.Include(d => d.Widgets).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Dashboard>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Dashboards.AsNoTracking()
            .Where(d => d.OwnerId == userId || d.Visibility == SavedFilterVisibility.Shared)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Dashboard>> GetOwnedAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Dashboards.Where(d => d.OwnerId == userId).ToListAsync(cancellationToken);

    public void Add(Dashboard dashboard) => db.Dashboards.Add(dashboard);

    public void Remove(Dashboard dashboard) => db.Dashboards.Remove(dashboard);
}
