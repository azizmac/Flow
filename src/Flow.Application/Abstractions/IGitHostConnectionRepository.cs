using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IGitHostConnectionRepository
{
    Task<IReadOnlyList<GitHostConnection>> GetAllAsync(CancellationToken cancellationToken);

    Task<GitHostConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(GitHostConnection connection);

    void Remove(GitHostConnection connection);
}
