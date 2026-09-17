using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Application.Abstractions;

/// <summary>
/// Отбор задач для сводного списка (GET /tasks). Все поля-фильтры необязательны: null — «не ограничивать».
/// BoardId = null означает «по всем проектам» — этим запрос и отличается от списка внутри доски.
/// AssigneeId = null не фильтрует по исполнителю вовсе; задачи без исполнителя отбирает Unassigned.
/// StatusId задаёт конкретный статус проекта, StatusType — тип статуса (наборы статусов у проектов разные,
/// и в сводном списке фильтровать можно только по типу).
/// Пагинация двух видов и они взаимоисключающие: keyset по (CreatedAt desc, Id desc) через
/// BeforeCreatedAt/BeforeId — для кнопки «показать ещё», и Offset — для таблицы со страницами и сортировкой.
/// Sort/Descending задают порядок; при keyset-пагинации применим только порядок по умолчанию (Created desc).
/// </summary>
public sealed record TaskListFilter(
    Guid? BoardId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    Guid? StatusId = null,
    StatusType? StatusType = null,
    string? Query = null,
    DateTime? BeforeCreatedAt = null,
    Guid? BeforeId = null,
    int Limit = 100,
    int? Offset = null,
    TaskSortField Sort = TaskSortField.Created,
    bool Descending = true);

/// <summary>
/// Счётчики по отбору: всего (без фильтра статуса), сколько попало под все фильтры (Matched),
/// по типу статуса (сводный список) и по конкретному статусу (список проекта).
/// Статусы без задач в списках отсутствуют — вызывающая сторона трактует это как 0.
/// </summary>
public sealed record TaskCounts(
    int Total,
    int Matched,
    IReadOnlyList<(StatusType? Type, int Count)> ByType,
    IReadOnlyList<(Guid StatusId, int Count)> ByStatus);
