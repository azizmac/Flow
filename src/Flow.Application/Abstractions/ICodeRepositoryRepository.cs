using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Граница Application для подключённых к проектам Git-репозиториев.</summary>
public interface ICodeRepositoryRepository
{
    Task<CodeRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeRepository>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken);

    /// <summary>Один и тот же remote нельзя подключить к одной доске дважды.</summary>
    Task<bool> ExistsByBoardAndRemoteUrlAsync(Guid boardId, string remoteUrl, CancellationToken cancellationToken);

    void Add(CodeRepository repository);

    void Remove(CodeRepository repository);
}
