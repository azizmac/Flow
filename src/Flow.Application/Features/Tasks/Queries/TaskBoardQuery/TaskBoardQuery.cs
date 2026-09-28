using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskBoardQuery;

/// <summary>
/// Канбан (docs/TZ_task_views.md §1). BoardId задан — колонки это статусы проекта по SortOrder, карточки по Rank;
/// null — «Все проекты»: колонки это виды статусов плюс «Другие» (статусы без вида), карточки по UpdatedAt.
/// Фильтры те же, что у списка (панель и FQL; ORDER BY из FQL здесь не действует — порядок задаёт ранг).
/// Финальные колонки показывают задачи, сменившие статус за Board.DoneColumnDays дней.
/// Без колонки — все колонки по первой странице; колонка (StatusId, StatusType или Other) — только она,
/// со страницы Offset: так догружается прокрутка. Невидимый или несуществующий проект — null (404).
/// </summary>
public sealed record TaskBoardQuery(
    Guid ActorId,
    Guid? BoardId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    string? Query = null,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    string? Fql = null,
    Guid? StatusId = null,
    StatusType? StatusType = null,
    bool Other = false,
    int Offset = 0,
    int? Limit = null) : IRequest<TaskBoardResponse?>;
