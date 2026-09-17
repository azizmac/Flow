using Flow.Shared.Contracts.Boards;

namespace Flow.Shared.Contracts.Tasks;

/// <summary>
/// Страница списка задач (GET /tasks). NextCursor = null — больше страниц нет (в offset-режиме всегда null).
/// Total и счётчики считаются по всему отбору, но без фильтра статуса: иначе кнопки фильтра показывали бы
/// число задач в уже выбранном статусе, а не то, сколько их найдётся при переключении.
/// Matched, наоборот, учитывает все фильтры — по нему таблица считает число страниц.
/// ByType нужен сводному списку (наборы статусов у проектов разные), ByStatus — списку внутри проекта.
/// </summary>
public sealed record TaskListResponse(
    IReadOnlyList<TaskResponse> Items,
    string? NextCursor,
    int Total,
    int Matched,
    IReadOnlyList<StatusTypeCount> ByType,
    IReadOnlyList<StatusCount> ByStatus);

/// <summary>Type = null — статус, добавленный в проект вручную и не попадающий ни в один из четырёх типов.</summary>
public sealed record StatusTypeCount(StatusType? Type, int Count);

public sealed record StatusCount(Guid StatusId, int Count);
