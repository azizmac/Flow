using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GitRepositoryBoardRepository(FlowDbContext db) : IGitRepositoryBoardRepository
{
    public async Task<IReadOnlyList<GitRepositoryBoard>> GetAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        await db.GitRepositoryBoards
            .Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId))
            .ToListAsync(cancellationToken);

    public void Add(GitRepositoryBoard binding) => db.GitRepositoryBoards.Add(binding);

    public void Remove(GitRepositoryBoard binding) => db.GitRepositoryBoards.Remove(binding);
}
