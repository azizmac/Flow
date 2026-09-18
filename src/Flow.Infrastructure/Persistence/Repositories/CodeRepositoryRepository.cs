using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class CodeRepositoryRepository(FlowDbContext db) : ICodeRepositoryRepository
{
    public Task<CodeRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.CodeRepositories.FirstOrDefaultAsync(repository => repository.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CodeRepository>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken) =>
        await db.CodeRepositories
            .Where(repository => repository.BoardId == boardId)
            .OrderBy(repository => repository.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsByBoardAndRemoteUrlAsync(Guid boardId, string remoteUrl, CancellationToken cancellationToken) =>
        db.CodeRepositories.AnyAsync(
            repository => repository.BoardId == boardId && repository.RemoteUrl == remoteUrl,
            cancellationToken);

    public void Add(CodeRepository repository) => db.CodeRepositories.Add(repository);

    public void Remove(CodeRepository repository) => db.CodeRepositories.Remove(repository);
}
