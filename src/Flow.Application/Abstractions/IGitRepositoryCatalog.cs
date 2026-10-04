using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IGitRepositoryCatalog
{
    Task<IReadOnlyList<GitRepository>> GetAllAsync(Guid? connectionId, CancellationToken cancellationToken);

    Task<GitRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<GitRepository?> FindAsync(Guid connectionId, string externalId, CancellationToken cancellationToken);

    void Add(GitRepository repository);

    void Remove(GitRepository repository);
}
