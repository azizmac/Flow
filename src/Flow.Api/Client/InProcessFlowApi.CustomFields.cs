using Flow.Application.Features.CustomFields;
using Flow.Client.Services;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.CustomFields;
using Flow.Shared.Contracts.Tasks;
using DomainFieldType = Flow.Domain.Entities.CustomFieldType;

namespace Flow.Api.Client;

/// <summary>Пользовательские поля (docs/TZ_task_model.md §4) — коды как у CustomFieldsController; ошибки ввода ловит Guard (400).</summary>
internal sealed partial class InProcessFlowApi
{
    public Task<ApiResult<BoardResponse>> CreateCustomField(Guid boardId, CreateCustomFieldRequest request, CancellationToken ct = default) =>
        SendBoard(actor => new CustomFieldCreateCommand(actor, boardId, request.Key, request.Name, (DomainFieldType)(int)request.Type,
            request.Options, request.IsRequired, request.TaskTypeIds), ct);

    public Task<ApiResult<BoardResponse>> UpdateCustomField(Guid boardId, Guid fieldId, UpdateCustomFieldRequest request, CancellationToken ct = default) =>
        SendBoard(actor => new CustomFieldUpdateCommand(actor, boardId, fieldId, request.Name,
            request.Options?.Select(o => (o.Id, o.Label, o.Color)).ToList(), request.IsRequired, request.TaskTypeIds, request.IsArchived), ct);

    public Task<ApiResult<BoardResponse>> ReorderCustomFields(Guid boardId, ReorderCustomFieldsRequest request, CancellationToken ct = default) =>
        SendBoard(actor => new CustomFieldReorderCommand(actor, boardId, request.FieldIds), ct);

    public Task<ApiResult<TaskResponse>> SetTaskCustomFields(Guid taskId, SetCustomFieldsRequest request, CancellationToken ct = default) =>
        SendUpdate(actor => new TaskSetCustomFieldsCommand(actor, taskId, request.Values), ct);

    private Task<ApiResult<BoardResponse>> SendBoard(Func<Guid, MediatR.IRequest<BoardResponse?>> request, CancellationToken ct) =>
        Scoped(async mediator =>
            await mediator.Send(request(await ActorAsync()), ct) is { } board ? Ok(board) : NotFound<BoardResponse>());
}
