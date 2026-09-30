using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GitRepositoryCatalog(FlowDbContext db) : IGitRepositoryCatalog
{
    public async Task<IReadOnlyList<GitRepository>> GetAllAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        await db.GitRepositories.Where(r => connectionId == null || r.ConnectionId == connectionId).OrderBy(r => r.FullName).ToListAsync(cancellationToken);

    public Task<GitRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.GitRepositories.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<GitRepository?> FindAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        db.GitRepositories.FirstOrDefaultAsync(r => r.ConnectionId == connectionId && r.ExternalId == externalId, cancellationToken);

    public void Add(GitRepository repository) => db.GitRepositories.Add(repository);

    public void Remove(GitRepository repository) => db.GitRepositories.Remove(repository);
}
