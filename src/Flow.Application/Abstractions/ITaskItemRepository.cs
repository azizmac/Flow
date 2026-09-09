using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface ITaskItemRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>assigneeId = null — все задачи доски; иначе только назначенные на этого пользователя.</summary>
    Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken);

    /// <summary>Нужно для валидации ChangeStatus — статус должен принадлежать той же доске, что и задача.</summary>
    Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}
