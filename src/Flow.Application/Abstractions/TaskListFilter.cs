using Flow.Domain.Entities;
using Flow.Shared.Contracts.Tasks;
using TaskPriority = Flow.Domain.Entities.TaskPriority;

namespace Flow.Application.Abstractions;

/// <summary>
/// Отбор задач для сводного списка (GET /tasks). Все поля-фильтры необязательны: null — «не ограничивать».
/// BoardId = null означает «по всем проектам» — этим запрос и отличается от списка внутри доски.
/// AssigneeId = null не фильтрует по исполнителю вовсе; задачи без исполнителя отбирает Unassigned.
/// StatusId задаёт конкретный статус проекта, StatusType — тип статуса (наборы статусов у проектов разные,
/// и в сводном списке фильтровать можно только по типу).
/// Пагинация двух видов и они взаимоисключающие: keyset по (CreatedAt desc, Id desc) через
/// BeforeCreatedAt/BeforeId — для кнопки «показать ещё», и Offset — для таблицы со страницами и сортировкой.
/// VisibleBoardIds — проекты, которые видит actor (docs/TZ_project_access.md, 4B); null — все: и счётчики, и страница
/// считаются только по ним, иначе «Все проекты» выдали бы число задач приватного проекта.
/// ParentId — только прямые подзадачи этой задачи (блок «Подзадачи», docs/TZ_task_model.md §3).
/// Condition — условие FQL после биндинга (docs/TZ_task_views.md §7), складывается с остальными полями через AND;
/// Orders — ORDER BY из FQL, в offset-режиме заменяет Sort/Descending.
/// UntypedStatus — только задачи в статусах без вида (колонка «Другие» сводного канбана); DoneWindowAt — «сейчас» для
/// окна финальной колонки канбана: задачи в финальных статусах остаются, только если сменили статус не раньше
/// Board.DoneColumnDays дней до этого момента (docs/TZ_task_views.md §1); null — окна нет, как в списке.
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
    bool Descending = true,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    IReadOnlyCollection<Guid>? VisibleBoardIds = null,
    Guid? ParentId = null,
    TaskFilterNode? Condition = null,
    IReadOnlyList<TaskOrder>? Orders = null,
    bool UntypedStatus = false,
    DateTime? DoneWindowAt = null);

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
