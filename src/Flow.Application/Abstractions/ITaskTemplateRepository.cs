using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Шаблоны задач проекта (этап 3G).</summary>
public interface ITaskTemplateRepository
{
    Task<TaskTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Шаблоны проекта по SortOrder.</summary>
    Task<IReadOnlyList<TaskTemplate>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken);

    void Add(TaskTemplate template);

    void Remove(TaskTemplate template);
}
