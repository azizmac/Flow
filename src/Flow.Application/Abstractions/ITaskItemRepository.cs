using Flow.Domain.Entities;
using Flow.Shared.Ids;

namespace Flow.Application.Abstractions;

public interface ITaskItemRepository
{
    Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(BoardId boardId, CancellationToken cancellationToken);

    /// <summary>Нужно для валидации ChangeStatus — статус должен принадлежать той же доске, что и задача.</summary>
    Task<bool> StatusBelongsToBoardAsync(StatusId statusId, BoardId boardId, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}
