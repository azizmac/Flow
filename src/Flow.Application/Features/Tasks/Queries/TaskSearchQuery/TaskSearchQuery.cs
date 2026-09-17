using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskSearchQuery;

/// <summary>
/// Сводный список задач. BoardId = null — по всем проектам; остальные поля — необязательные фильтры.
/// Cursor берётся из <see cref="TaskListResponse.NextCursor"/> предыдущей страницы; Offset — альтернативный
/// способ листания для таблицы с сортировкой (взаимоисключающи, приоритет у Offset).
/// </summary>
public sealed record TaskSearchQuery(
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
    bool Descending = true) : IRequest<TaskListResponse>;
