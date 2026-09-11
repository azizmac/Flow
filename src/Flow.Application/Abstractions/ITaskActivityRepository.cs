using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Журнал изменений задачи — только добавление и чтение, записи не правятся и не удаляются.</summary>
public interface ITaskActivityRepository
{
    /// <summary>Записи задачи по возрастанию CreatedAt.</summary>
    Task<IReadOnlyList<TaskActivity>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    void Add(TaskActivity activity);
}
