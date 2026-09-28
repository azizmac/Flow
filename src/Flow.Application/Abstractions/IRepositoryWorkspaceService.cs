using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface IRepositoryWorkspaceService
{
    Task<string> SynchronizeAsync(CodeRepository repository, CancellationToken cancellationToken);

    string GetAgentDirectory(CodeRepository repository);

    Task DeleteBoardAsync(Guid boardId, CancellationToken cancellationToken);
}
