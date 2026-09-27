using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Повторения задач (docs/TZ_task_model.md §9).</summary>
public interface ITaskRecurrenceRepository
{
    Task<TaskRecurrence?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<TaskRecurrence?> GetByTemplateAsync(Guid templateTaskId, CancellationToken cancellationToken);

    /// <summary>Id активных правил — генератор обходит их по одному, каждое в своей области.</summary>
    Task<IReadOnlyList<Guid>> GetActiveIdsAsync(CancellationToken cancellationToken);

    /// <summary>Какие задачи из набора — образцы с правилом (значок в списке).</summary>
    Task<IReadOnlySet<Guid>> GetTemplatesAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    Task<bool> OccurrenceExistsAsync(Guid recurrenceId, DateOnly occursOn, CancellationToken cancellationToken);

    void Add(TaskRecurrence recurrence);

    void Remove(TaskRecurrence recurrence);

    void AddOccurrence(TaskRecurrenceOccurrence occurrence);
}
