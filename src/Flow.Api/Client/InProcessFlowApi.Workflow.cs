using Flow.Application.Features.Boards.Workflow;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;

namespace Flow.Api.Client;

/// <summary>Workflow проекта и доступные переходы задачи (docs/TZ_workflow_config.md §2) — коды как у контроллеров.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<WorkflowResponse>> GetWorkflow(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new WorkflowGetQuery(await ActorAsync(), boardId), ct) is { } workflow ? Ok(workflow) : NotFound<WorkflowResponse>());

    public Task<ApiResult<WorkflowResponse>> SetWorkflow(Guid boardId, SetWorkflowRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new WorkflowSetCommand(await ActorAsync(), boardId, request.Mode, request.Transitions, request.Layout), ct) is { } workflow
                ? Ok(workflow)
                : NotFound<WorkflowResponse>());

    public Task<ApiResult<IReadOnlyList<TaskTransitionResponse>>> GetTransitions(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskTransitionsQuery(await ActorAsync(), taskId), ct) is { } transitions
                ? Ok(transitions)
                : NotFound<IReadOnlyList<TaskTransitionResponse>>());
}
