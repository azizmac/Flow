using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Спринты проектов (docs/TZ_task_views.md §2).</summary>
public interface ISprintRepository
{
    /// <summary>Спринт со снимками обязательств, отслеживаемый.</summary>
    Task<Sprint?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Спринты проекта: активный, запланированные по SortOrder, затем завершённые от новых к старым.</summary>
    Task<IReadOnlyList<Sprint>> GetByBoardAsync(Guid boardId, bool includeCompleted, CancellationToken cancellationToken);

    /// <summary>Есть ли в проекте активный спринт, кроме <paramref name="exceptId"/>.</summary>
    Task<bool> HasActiveAsync(Guid boardId, Guid? exceptId, CancellationToken cancellationToken);

    Task<int> NextSortOrderAsync(Guid boardId, CancellationToken cancellationToken);

    void Add(Sprint sprint);

    void Remove(Sprint sprint);
}
