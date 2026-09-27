using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Связи между задачами (docs/TZ_task_model.md §5). Удаление задачи уносит её связи каскадом БД.</summary>
public interface ITaskLinkRepository
{
    Task<TaskLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Связи обоих направлений, от старых к новым.</summary>
    Task<IReadOnlyList<TaskLink>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(Guid sourceTaskId, Guid targetTaskId, TaskLinkType type, CancellationToken cancellationToken);

    /// <summary>
    /// Сколько незакрытых задач блокирует каждую из <paramref name="taskIds"/>: входящие Blocks от задач не в финальном
    /// статусе. Одним GROUP BY; незаблокированных в словаре нет. Признак «заблокирована» не хранится — считается.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountBlockersAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    /// <summary>Кого блокируют задачи <paramref name="sourceTaskIds"/> (исходящие Blocks) — шаг обхода для предупреждения о цикле.</summary>
    Task<IReadOnlyList<Guid>> GetBlockedTargetsAsync(IReadOnlyCollection<Guid> sourceTaskIds, CancellationToken cancellationToken);

    void Add(TaskLink link);

    void Remove(TaskLink link);
}
