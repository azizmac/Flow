using Flow.Application.Features.Milestones;
using Flow.Client.Services;
using Flow.Shared.Contracts.Milestones;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Вехи (docs/TZ_task_views.md §6) — коды как у MilestonesController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<MilestoneResponse>>> GetMilestones(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new MilestoneListQuery(await ActorAsync(), boardId), ct) is { } list ? Ok(list) : NotFound<IReadOnlyList<MilestoneResponse>>());

    public Task<ApiResult<MilestoneResponse>> GetMilestone(Guid milestoneId, CancellationToken ct = default) =>
        SendMilestone(actor => new MilestoneGetQuery(actor, milestoneId), ct);

    public Task<ApiResult<MilestoneResponse>> CreateMilestone(Guid boardId, CreateMilestoneRequest request, CancellationToken ct = default) =>
        SendMilestone(actor => new MilestoneCreateCommand(actor, boardId, request.Name, request.Description, request.TargetDate), ct);

    public Task<ApiResult<MilestoneResponse>> UpdateMilestone(Guid milestoneId, UpdateMilestoneRequest request, CancellationToken ct = default) =>
        SendMilestone(actor => new MilestoneUpdateCommand(actor, milestoneId, request.Name, request.Description, request.ClearDescription,
            request.TargetDate, request.ClearTargetDate, request.Closed), ct);

    public Task<ApiResult<MilestoneResponse>> ShareMilestone(Guid milestoneId, ShareMilestoneRequest request, CancellationToken ct = default) =>
        SendMilestone(actor => new MilestoneShareCommand(actor, milestoneId, request.BoardIds), ct);

    public Task<ApiResult<bool>> DeleteMilestone(Guid milestoneId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new MilestoneDeleteCommand(await ActorAsync(), milestoneId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<TaskResponse>> SetTaskTeam(Guid taskId, SetTaskTeamRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new Flow.Application.Features.Groups.TaskSetTeamCommand(actor, taskId, request.TeamId), ct);

    public Task<ApiResult<TaskResponse>> SetTaskMilestone(Guid taskId, SetTaskMilestoneRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetMilestoneCommand(actor, taskId, request.MilestoneId), ct);

    private Task<ApiResult<MilestoneResponse>> SendMilestone(Func<Guid, MediatR.IRequest<MilestoneResponse?>> request, CancellationToken ct) =>
        Scoped(async mediator =>
            await mediator.Send(request(await ActorAsync()), ct) is { } milestone ? Ok(milestone) : NotFound<MilestoneResponse>());
}
