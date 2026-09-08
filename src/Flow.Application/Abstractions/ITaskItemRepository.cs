using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface ITaskItemRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken);

    /// <summary>Нужно для валидации ChangeStatus — статус должен принадлежать той же доске, что и задача.</summary>
    Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}
