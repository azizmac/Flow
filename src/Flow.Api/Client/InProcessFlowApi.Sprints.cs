using Flow.Application.Features.Sprints.Commands.SprintCompleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintCreateCommand;
using Flow.Application.Features.Sprints.Commands.SprintDeleteCommand;
using Flow.Application.Features.Sprints.Commands.SprintStartCommand;
using Flow.Application.Features.Sprints.Commands.SprintUpdateCommand;
using Flow.Application.Features.Sprints.Commands.TaskSetSprintCommand;
using Flow.Application.Features.Sprints.Queries.BacklogQuery;
using Flow.Application.Features.Sprints.Queries.SprintListQuery;
using Flow.Application.Features.Sprints.Queries.SprintReportQuery;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Sprints;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Спринты и бэклог (docs/TZ_task_views.md §2) — коды как у SprintsController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<SprintResponse>>> GetSprints(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new SprintListQuery(await ActorAsync(), boardId), ct) is { } list ? Ok(list) : NotFound<IReadOnlyList<SprintResponse>>());

    public Task<ApiResult<SprintResponse>> CreateSprint(Guid boardId, CreateSprintRequest request, CancellationToken ct = default) =>
        SendSprint(actor => new SprintCreateCommand(actor, boardId, request.Name, request.Goal, request.StartDate, request.EndDate), ct);

    public Task<ApiResult<SprintResponse>> UpdateSprint(Guid sprintId, UpdateSprintRequest request, CancellationToken ct = default) =>
        SendSprint(actor => new SprintUpdateCommand(actor, sprintId, request.Name, request.Goal, request.ClearGoal, request.StartDate, request.EndDate, request.ClearDates), ct);

    public Task<ApiResult<SprintResponse>> StartSprint(Guid sprintId, StartSprintRequest request, CancellationToken ct = default) =>
        SendSprint(actor => new SprintStartCommand(actor, sprintId, request.StartDate, request.EndDate), ct);

    public Task<ApiResult<SprintResponse>> CompleteSprint(Guid sprintId, CompleteSprintRequest request, CancellationToken ct = default) =>
        SendSprint(actor => new SprintCompleteCommand(actor, sprintId, request.MoveOpenTo), ct);

    public Task<ApiResult<bool>> DeleteSprint(Guid sprintId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new SprintDeleteCommand(await ActorAsync(), sprintId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<SprintReportResponse>> GetSprintReport(Guid sprintId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new SprintReportQuery(await ActorAsync(), sprintId), ct) is { } report ? Ok(report) : NotFound<SprintReportResponse>());

    public Task<ApiResult<BacklogResponse>> GetBacklog(
        Guid boardId,
        Guid? assigneeId = null,
        bool unassigned = false,
        string? query = null,
        TaskTypeKind? typeKind = null,
        TaskPriority? priority = null,
        string? fql = null,
        Guid? epicId = null,
        CancellationToken ct = default) =>
        Scoped(async mediator =>
        {
            var backlog = await mediator.Send(
                new BacklogQuery(await ActorAsync(), boardId, assigneeId, unassigned, string.IsNullOrWhiteSpace(query) ? null : query.Trim(),
                    typeKind, priority, string.IsNullOrWhiteSpace(fql) ? null : fql, epicId),
                ct);
            return backlog is null ? NotFound<BacklogResponse>() : Ok(backlog);
        });

    public Task<ApiResult<TaskResponse>> SetTaskSprint(Guid taskId, SetTaskSprintRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetSprintCommand(actor, taskId, request.SprintId), ct);

    private Task<ApiResult<SprintResponse>> SendSprint(Func<Guid, MediatR.IRequest<SprintResponse?>> command, CancellationToken ct) =>
        Scoped(async mediator =>
            await mediator.Send(command(await ActorAsync()), ct) is { } sprint ? Ok(sprint) : NotFound<SprintResponse>());
}
