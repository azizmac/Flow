using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Журнал изменений задачи — только добавление и чтение, записи не правятся и не удаляются.</summary>
public interface ITaskActivityRepository
{
    /// <summary>Записи задачи по возрастанию CreatedAt.</summary>
    Task<IReadOnlyList<TaskActivity>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Записи этих задач заданных типов по возрастанию CreatedAt — burndown спринта (docs/TZ_task_views.md §2).</summary>
    Task<IReadOnlyList<TaskActivity>> GetByTaskIdsAsync(IReadOnlyCollection<Guid> taskIds, IReadOnlyCollection<TaskActivityType> types, CancellationToken cancellationToken);

    /// <summary>Задачи, у которых в журнале есть запись этого типа со значением (старым или новым) — кто бывал в спринте.</summary>
    Task<IReadOnlyList<Guid>> GetTaskIdsWithValueAsync(TaskActivityType type, string value, CancellationToken cancellationToken);

    void Add(TaskActivity activity);
}
