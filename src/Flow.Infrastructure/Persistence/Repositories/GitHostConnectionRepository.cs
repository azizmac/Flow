using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GitHostConnectionRepository(FlowDbContext db) : IGitHostConnectionRepository
{
    public async Task<IReadOnlyList<GitHostConnection>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.ScmConnections.OrderBy(c => c.Name).ToListAsync(cancellationToken);

    public Task<GitHostConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.ScmConnections.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public void Add(GitHostConnection connection) => db.ScmConnections.Add(connection);

    public void Remove(GitHostConnection connection) => db.ScmConnections.Remove(connection);
}
