using Flow.Application.Features.Tasks.Restructure;
using Flow.Client.Services;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Слияние, разделение, перенос (docs/TZ_task_model.md §6) — коды как у TaskRestructureController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<TaskResponse>> GetTaskByCode(string code, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskGetByCodeQuery(await ActorAsync(), code), ct) is { } task ? Ok(task) : NotFound<TaskResponse>());

    public Task<ApiResult<TaskResponse>> MergeTask(Guid taskId, MergeTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskMergeCommand(await ActorAsync(), taskId, request.TargetId), ct) is { } task ? Ok(task) : NotFound<TaskResponse>());

    public Task<ApiResult<IReadOnlyList<TaskResponse>>> SplitTask(Guid taskId, SplitTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskSplitCommand(await ActorAsync(), taskId, request.Parts), ct) is { } created ? Ok(created) : NotFound<IReadOnlyList<TaskResponse>>());

    public Task<ApiResult<TaskMovePreviewResponse>> PreviewTaskMove(Guid taskId, MoveTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskMovePreviewQuery(await ActorAsync(), taskId, request.BoardId, request.StatusMap, request.TypeMap), ct) is { } preview
                ? Ok(preview)
                : NotFound<TaskMovePreviewResponse>());

    public Task<ApiResult<TaskResponse>> MoveTask(Guid taskId, MoveTaskRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskMoveCommand(await ActorAsync(), taskId, request.BoardId, request.StatusMap, request.TypeMap), ct) is { } task
                ? Ok(task)
                : NotFound<TaskResponse>());
}
