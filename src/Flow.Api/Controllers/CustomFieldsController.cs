using Flow.Application.Abstractions;
using Flow.Application.Features.CustomFields;
using Flow.Shared.Contracts.CustomFields;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using DomainFieldType = Flow.Domain.Entities.CustomFieldType;

namespace Flow.Api.Controllers;

/// <summary>
/// Пользовательские поля (docs/TZ_task_model.md §4): "boards/{id}/custom-fields" — определения проекта (ManageConfig,
/// ответ — проект целиком, как у типов задач), "tasks/{id}/custom-fields" — значения задачи (правка задачи).
/// </summary>
[ApiController]
public class CustomFieldsController(IMediator mediator, IActorAccessor actor) : ControllerBase
{
    /// <summary>400 — ключ не по правилам или занят, пустой список у Select, варианты у другого типа, чужой тип задачи.</summary>
    [HttpPost("boards/{boardId:guid}/custom-fields")]
    public Task<IActionResult> Create(Guid boardId, CreateCustomFieldRequest request, CancellationToken cancellationToken) =>
        !Enum.IsDefined(request.Type)
            ? Task.FromResult<IActionResult>(BadRequest(new { Message = $"Unknown custom field type {request.Type}." }))
            : Send(new CustomFieldCreateCommand(actor.Require(), boardId, request.Key, request.Name, (DomainFieldType)(int)request.Type,
                request.Options, request.IsRequired, request.TaskTypeIds), cancellationToken);

    [HttpPatch("boards/{boardId:guid}/custom-fields/{fieldId:guid}")]
    public Task<IActionResult> Update(Guid boardId, Guid fieldId, UpdateCustomFieldRequest request, CancellationToken cancellationToken) =>
        Send(new CustomFieldUpdateCommand(actor.Require(), boardId, fieldId, request.Name,
            request.Options?.Select(o => (o.Id, o.Label, o.Color)).ToList(), request.IsRequired, request.TaskTypeIds, request.IsArchived), cancellationToken);

    [HttpPut("boards/{boardId:guid}/custom-fields/order")]
    public Task<IActionResult> Reorder(Guid boardId, ReorderCustomFieldsRequest request, CancellationToken cancellationToken) =>
        Send(new CustomFieldReorderCommand(actor.Require(), boardId, request.FieldIds), cancellationToken);

    /// <summary>Значения полей задачи; 400 — значение не того вида, чужое поле, очистка обязательного, неактивный человек.</summary>
    [HttpPatch("tasks/{id:guid}/custom-fields")]
    public async Task<IActionResult> SetValues(Guid id, SetCustomFieldsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await mediator.Send(new TaskSetCustomFieldsCommand(actor.Require(), id, request.Values), cancellationToken);
            return result.IsNotFound ? NotFound() : Ok(result.Response);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }

    private async Task<IActionResult> Send(IRequest<Flow.Shared.Contracts.Boards.BoardResponse?> command, CancellationToken cancellationToken)
    {
        try
        {
            return await mediator.Send(command, cancellationToken) is { } board ? Ok(board) : NotFound();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return BadRequest(new { ex.Message });
        }
    }
}
