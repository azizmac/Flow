using Flow.Application.Features.TaskTemplates;
using Flow.Client.Services;
using Flow.Shared.Contracts.Tasks;

namespace Flow.Api.Client;

/// <summary>Шаблоны задач (этап 3G) — как TaskTemplatesController.</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<IReadOnlyList<TaskTemplateResponse>>> GetTaskTemplates(Guid boardId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskTemplateListQuery(await ActorAsync(), boardId), ct) is { } list
            ? Ok(list)
            : NotFound<IReadOnlyList<TaskTemplateResponse>>());

    public Task<ApiResult<TaskTemplateResponse>> CreateTaskTemplate(Guid boardId, SaveTaskTemplateRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskTemplateCreateCommand(await ActorAsync(), boardId, request), ct) is { } t
            ? Ok(t)
            : NotFound<TaskTemplateResponse>());

    public Task<ApiResult<TaskTemplateResponse>> UpdateTaskTemplate(Guid templateId, SaveTaskTemplateRequest request, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskTemplateUpdateCommand(await ActorAsync(), templateId, request), ct) is { } t
            ? Ok(t)
            : NotFound<TaskTemplateResponse>());

    public Task<ApiResult<bool>> DeleteTaskTemplate(Guid templateId, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskTemplateDeleteCommand(await ActorAsync(), templateId), ct) ? Ok(true) : NotFound<bool>());

    public Task<ApiResult<TaskTemplateResponse>> SaveTaskAsTemplate(Guid taskId, string name, CancellationToken ct = default) =>
        Scoped(async mediator => await mediator.Send(new TaskTemplateFromTaskCommand(await ActorAsync(), taskId, name), ct) is { } t
            ? Ok(t)
            : NotFound<TaskTemplateResponse>());
}
