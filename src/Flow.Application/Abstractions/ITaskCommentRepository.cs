using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface ITaskCommentRepository
{
    /// <summary>Комментарий вместе с упоминаниями, отслеживаемый (для правки/удаления).</summary>
    Task<TaskComment?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Комментарии задачи по возрастанию CreatedAt.</summary>
    Task<IReadOnlyList<TaskComment>> GetByTaskIdAsync(Guid taskId, CancellationToken cancellationToken);

    /// <summary>Число комментариев по задачам одним GROUP BY — для TaskResponse.CommentCount. Задач без комментариев в словаре нет.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByTaskIdsAsync(IReadOnlyCollection<Guid> taskIds, CancellationToken cancellationToken);

    void Add(TaskComment comment);

    void Remove(TaskComment comment);
}
