using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

public interface ITaskItemRepository
{
    Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>assigneeId = null — все задачи доски; иначе только назначенные на этого пользователя.</summary>
    Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken);

    /// <summary>Задачи в статусе — отслеживаемые: их переводят в другой статус при удалении этого (docs/TZ_workflow_config.md §1).</summary>
    Task<IReadOnlyList<TaskItem>> GetByStatusIdAsync(Guid statusId, CancellationToken cancellationToken);

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

    /// <summary>Прямые подзадачи — отслеживаемые (смена типа родителя проверяет их уровни, каскадное удаление их удаляет).</summary>
    Task<IReadOnlyList<TaskItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken);

    /// <summary>
    /// Число прямых подзадач и сколько из них в финальном статусе — одним GROUP BY (docs/TZ_task_model.md §3),
    /// как CommentCount. Задачи без детей в словаре отсутствуют.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, ChildCounts>> CountChildrenAsync(IReadOnlyCollection<Guid> parentIds, CancellationToken cancellationToken);

    /// <summary>
    /// Дерево в порядке обхода (родитель, затем его поддерево) с глубиной: <paramref name="rootId"/> = null — весь проект
    /// от задач верхнего уровня (глубина 0), иначе сама задача (глубина 0) и её поддерево. Внутри уровня — по рангу.
    /// </summary>
    Task<IReadOnlyList<TaskTreeEntry>> GetTreeAsync(Guid boardId, Guid? rootId, CancellationToken cancellationToken);

    /// <summary>Максимальный ранг в проекте из БД (не из отслеживаемых сущностей); null — задач нет.</summary>
    Task<string?> GetMaxRankAsync(Guid boardId, Guid? excludeTaskId, CancellationToken cancellationToken);

    /// <summary>
    /// Ближайший ранг в проекте по ту сторону от <paramref name="rank"/>: <paramref name="after"/> = true — наименьший
    /// больший, false — наибольший меньший. <paramref name="excludeTaskId"/> — перемещаемая задача, её старое место
    /// соседом не считается. null — край проекта.
    /// </summary>
    Task<string?> GetNeighborRankAsync(Guid boardId, string rank, bool after, Guid excludeTaskId, CancellationToken cancellationToken);

    void Add(TaskItem task);

    void Remove(TaskItem task);
}
