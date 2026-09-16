using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface ITaskItemRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>assigneeId = null — все задачи доски; иначе только назначенные на этого пользователя.</summary>
    Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken);

    /// <summary>
    /// Страница задач по отбору, от новых к старым. Возвращает не больше <see cref="TaskListFilter.Limit"/> задач;
    /// «есть ли ещё» вызывающая сторона определяет по тому, заполнилась ли страница целиком.
    /// </summary>
    Task<IReadOnlyList<TaskItem>> SearchAsync(TaskListFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Сколько задач найдётся по отбору — всего, по типу статуса и по конкретному статусу. Одним проходом,
    /// без выгрузки задач. Фильтры статуса в <paramref name="filter"/> игнорируются: счётчики нужны кнопкам
    /// переключения статуса, и они показывают, сколько задач найдётся при переключении.
    /// </summary>
    Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Количество задач по каждой доске одним запросом (GROUP BY BoardId) — для BoardResponse.TaskCount.
    /// Доски без задач в словаре отсутствуют, вызывающая сторона трактует это как 0.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByBoardIdsAsync(IReadOnlyCollection<Guid> boardIds, CancellationToken cancellationToken);

    /// <summary>Задача по коду (PROJ-142) — прямое попадание из строки поиска. Регистр не важен.</summary>
    Task<TaskItem?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Нужно для валидации ChangeStatus — статус должен принадлежать той же доске, что и задача.</summary>
    Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}
