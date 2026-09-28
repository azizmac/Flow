using Flow.Shared.Contracts.Boards;

namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Колонка канбана (docs/TZ_task_views.md §1). В проекте колонка — статус (StatusId), в режиме «Все проекты» —
/// вид статуса (StatusType), а статусы без вида собираются в колонку Other. Count — сколько задач в колонке всего
/// под фильтром (в финальной — только за окно DoneColumnDays), Tasks — страница карточек по порядку.
/// </summary>
public sealed record TaskBoardColumn(Guid? StatusId, StatusType? StatusType, bool Other, int Count, IReadOnlyList<TaskResponse> Tasks);

/// <summary>Канбан целиком или одна колонка (при догрузке). DoneColumnDays — окно проекта; в «Все проекты» null (у проектов своё).</summary>
public sealed record TaskBoardResponse(Guid? BoardId, int? DoneColumnDays, IReadOnlyList<TaskBoardColumn> Columns);

/// <summary>PUT /boards/{id}/done-column-days.</summary>
public sealed record SetDoneColumnDaysRequest(int Days);
