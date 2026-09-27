using Flow.Shared.Contracts.Tasks;
using MediatR;
using TaskPriority = Flow.Domain.Entities.TaskPriority;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Response = null, если доска не найдена.</summary>
/// <remarks>
/// ActorId — Member+ (403 для Reader); пишется в TaskItem.CreatedById. TypeId = null — тип проекта по умолчанию,
/// чужой или архивный тип — InvalidOperationException (400). Priority = null — None.
/// </remarks>
public sealed record TaskCreateCommand(
    Guid ActorId,
    Guid BoardId,
    string Title,
    string? Description,
    Guid? StatusId,
    Guid? TypeId = null,
    TaskPriority? Priority = null)
    : IRequest<TaskResponse?>;
