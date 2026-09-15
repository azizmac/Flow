using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>
/// Отбор задач для сводного списка (GET /tasks). Все поля-фильтры необязательны: null — «не ограничивать».
/// BoardId = null означает «по всем проектам» — этим запрос и отличается от списка внутри доски.
/// AssigneeId = null не фильтрует по исполнителю вовсе; задачи без исполнителя отбирает Unassigned.
/// StatusId задаёт конкретный статус проекта, StatusType — тип статуса (наборы статусов у проектов разные,
/// и в сводном списке фильтровать можно только по типу).
/// Пагинация — keyset по (CreatedAt desc, Id desc): BeforeCreatedAt/BeforeId — позиция последней выданной задачи.
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
    int Limit = 100);

/// <summary>
/// Счётчики по отбору: всего, по типу статуса (сводный список) и по конкретному статусу (список проекта).
/// Статусы без задач в списках отсутствуют — вызывающая сторона трактует это как 0.
/// </summary>
public sealed record TaskCounts(
    int Total,
    IReadOnlyList<(StatusType? Type, int Count)> ByType,
    IReadOnlyList<(Guid StatusId, int Count)> ByStatus);
