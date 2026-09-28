namespace Flow.Shared.Contracts.CodeRepositories;

public sealed record CodeRepositoryResponse(
    Guid Id,
    Guid BoardId,
    string Provider,
    string Name,
    string RemoteUrl,
    string Branch,
    string SyncState,
    string? LastSyncedCommit,
    DateTime? LastSyncedAt,
    string? LastSyncError);
