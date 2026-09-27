using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Сохранённые фильтры и избранное (docs/TZ_task_views.md §7).</summary>
public interface ISavedFilterRepository
{
    Task<SavedFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Свои и общие, по имени.</summary>
    Task<IReadOnlyList<SavedFilter>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlySet<Guid>> GetStarredIdsAsync(Guid userId, CancellationToken cancellationToken);

    Task<SavedFilterStar?> GetStarAsync(Guid filterId, Guid userId, CancellationToken cancellationToken);

    void Add(SavedFilter filter);

    void Remove(SavedFilter filter);

    void AddStar(SavedFilterStar star);

    void RemoveStar(SavedFilterStar star);
}
