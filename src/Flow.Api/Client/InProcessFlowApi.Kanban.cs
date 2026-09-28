using Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;
using Flow.Application.Features.Tasks.Queries.TaskBoardQuery;
using Flow.Application.Features.Tasks.Queries.TaskCalendarQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Канбан (docs/TZ_task_views.md §1) — коды как у контроллеров.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<TaskBoardResponse>> GetTaskBoard(
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        string? fql = null,
        Guid? statusId = null,
        StatusType? statusType = null,
        bool other = false,
        int offset = 0,
        int? limit = null,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var text = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
            var response = await mediator.Send(
                new TaskBoardQuery(await ActorAsync(), boardId, assigneeId, unassigned, text, typeKind, priority,
                    string.IsNullOrWhiteSpace(fql) ? null : fql, statusId, statusType, other, offset, limit),
                ct);

            return response is null ? NotFound<TaskBoardResponse>() : Ok(response);
        });

    /// <summary>Календарь (docs/TZ_task_views.md §5) — задачи окна дат.</summary>
    public Task<ApiResult<TaskCalendarResponse>> GetCalendar(
        DateOnly from,
        DateOnly to,
        Guid? boardId = null,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        string? fql = null,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var response = await mediator.Send(
                new TaskCalendarQuery(await ActorAsync(), from, to, boardId, assigneeId, unassigned,
                    string.IsNullOrWhiteSpace(query) ? null : query.Trim(), typeKind, priority, string.IsNullOrWhiteSpace(fql) ? null : fql),
                ct);
            return response is null ? NotFound<TaskCalendarResponse>() : Ok(response);
        });

    public Task<ApiResult<BoardResponse>> SetDoneColumnDays(Guid boardId, int days, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new BoardDoneColumnDaysSetCommand(await ActorAsync(), boardId, days), ct) is { } board
                ? Ok(board)
                : NotFound<BoardResponse>());
}
