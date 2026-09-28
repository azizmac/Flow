using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class PermissionSetRepository(FlowDbContext db) : IPermissionSetRepository
{
    public Task<PermissionSet?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.PermissionSets.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PermissionSet>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.PermissionSets.OrderBy(s => s.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PermissionSet>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.PermissionSets.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(cancellationToken);

    public void Add(PermissionSet set) => db.PermissionSets.Add(set);

    public void Remove(PermissionSet set) => db.PermissionSets.Remove(set);
}
