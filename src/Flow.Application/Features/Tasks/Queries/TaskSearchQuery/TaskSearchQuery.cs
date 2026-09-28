using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskSearchQuery;

/// <summary>
/// Сводный список задач. BoardId = null — по всем проектам; остальные поля — необязательные фильтры.
/// Cursor берётся из <see cref="TaskListResponse.NextCursor"/> предыдущей страницы; Offset — альтернативный
/// способ листания для таблицы с сортировкой (взаимоисключающи, приоритет у Offset).
/// Fql — запрос на FQL (docs/TZ_task_views.md §7): складывается с остальными фильтрами через AND, его ORDER BY
/// заменяет Sort. Ошибка синтаксиса или неизвестное имя — FqlException (400 с позицией).
/// </summary>
public sealed record TaskSearchQuery(
    Guid ActorId,
    Guid? BoardId = null,
    Guid? AssigneeId = null,
    bool Unassigned = false,
    Guid? StatusId = null,
    StatusType? StatusType = null,
    string? Query = null,
    int? Limit = null,
    string? Cursor = null,
    int? Offset = null,
    TaskSortField Sort = TaskSortField.Created,
    bool Descending = true,
    TaskTypeKind? TypeKind = null,
    TaskPriority? Priority = null,
    Guid? ParentId = null,
    string? Fql = null) : IRequest<TaskListResponse>;
