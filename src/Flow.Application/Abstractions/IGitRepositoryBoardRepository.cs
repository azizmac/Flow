using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IGitRepositoryBoardRepository
{
    /// <summary>Привязки: к проекту (boardId) или репозитория (repositoryId).</summary>
    Task<IReadOnlyList<GitRepositoryBoard>> GetAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken);

    void Add(GitRepositoryBoard binding);

    void Remove(GitRepositoryBoard binding);
}
