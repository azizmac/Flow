using Flow.Shared.Contracts.Tasks;
using MediatR;
using TaskPriority = Flow.Domain.Entities.TaskPriority;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Response = null, если доска не найдена.</summary>
/// <remarks>
/// ActorId — Member+ (403 для Reader); пишется в TaskItem.CreatedById. TypeId = null — тип проекта по умолчанию,
/// чужой или архивный тип — InvalidOperationException (400). Priority = null — None. ParentId — сразу подзадачей:
/// родитель из того же проекта и выше по уровню типа, иначе 400 (docs/TZ_task_model.md §3).
/// </remarks>
public sealed record TaskCreateCommand(
    Guid ActorId,
    Guid BoardId,
    string Title,
    string? Description,
    Guid? StatusId,
    Guid? TypeId = null,
    TaskPriority? Priority = null,
    Guid? ParentId = null,
    IReadOnlyDictionary<Guid, System.Text.Json.JsonElement?>? CustomFields = null,
    Guid? AssigneeId = null,
    Guid? TemplateId = null)
    : IRequest<TaskResponse?>;
