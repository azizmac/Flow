using Flow.Domain.Entities;
using Flow.Shared.Contracts.CodeRepositories;

namespace Flow.Application.Features.CodeRepositories;

internal static class CodeRepositoryMapping
{
    public static CodeRepositoryResponse ToResponse(this CodeRepository repository) => new(
        repository.Id,
        repository.BoardId,
        repository.Provider.ToString(),
        repository.Name,
        repository.RemoteUrl,
        repository.Branch,
        repository.SyncState.ToString(),
        repository.LastSyncedCommit,
        repository.LastSyncedAt,
        repository.LastSyncError);
}
