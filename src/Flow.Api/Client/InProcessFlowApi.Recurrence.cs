using Flow.Application.Features.Tasks.Recurrence;
using Flow.Client.Services;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Повторение задачи (docs/TZ_task_model.md §9) — коды как у TaskRecurrenceController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<TaskRecurrenceResponse>> GetRecurrence(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskRecurrenceGetQuery(await ActorAsync(), taskId), ct) is { } rule ? Ok(rule) : NotFound<TaskRecurrenceResponse>());

    public Task<ApiResult<TaskRecurrenceResponse>> SetRecurrence(Guid taskId, TaskRecurrenceRequest request, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskRecurrenceSetCommand(await ActorAsync(), taskId, request), ct) is { } rule ? Ok(rule) : NotFound<TaskRecurrenceResponse>());

    public Task<ApiResult<bool>> DeleteRecurrence(Guid taskId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskRecurrenceDeleteCommand(await ActorAsync(), taskId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<IReadOnlyList<DateOnly>>> PreviewRecurrence(Guid taskId, TaskRecurrenceRequest request, int count = 5, CancellationToken ct = default) =>
        Scoped(async mediator =>
            await mediator.Send(new TaskRecurrencePreviewQuery(await ActorAsync(), taskId, request, count), ct) is { } dates ? Ok(dates) : NotFound<IReadOnlyList<DateOnly>>());
}
