using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Abstractions;

public interface IGitDevelopmentLinkRepository
{
    Task<GitDevelopmentLink?> FindAsync(Guid taskId, Guid repositoryId, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken);

    /// <summary>Ветки репозитория с этим именем — у всех задач (удаление ветки закрывает их связи).</summary>
    Task<IReadOnlyList<GitDevelopmentLink>> GetByBranchAsync(Guid repositoryId, string branch, CancellationToken cancellationToken);

    Task<IReadOnlyList<GitDevelopmentLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Состояние последнего PR каждой задачи — значок в списке. Нет PR — задачи в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, GitDevelopmentLinkState>> GetLatestPullRequestStatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    void Add(GitDevelopmentLink link);
}
