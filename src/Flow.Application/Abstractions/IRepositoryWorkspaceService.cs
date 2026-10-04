using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IRepositoryWorkspaceService
{
    Task<string> SynchronizeAsync(GitRepository repository, CancellationToken cancellationToken);

    string GetAgentDirectory(GitRepository repository);

    Task<string> CreateAnalysisDirectoryAsync(IReadOnlyList<GitRepository> repositories, CancellationToken cancellationToken);
}
