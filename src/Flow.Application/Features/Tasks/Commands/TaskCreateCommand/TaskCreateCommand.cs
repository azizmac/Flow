using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Response = null, если доска не найдена.</summary>
/// <remarks>ActorId — Member+ (403 для Reader); пишется в TaskItem.CreatedById.</remarks>
public sealed record TaskCreateCommand(Guid ActorId, Guid BoardId, string Title, string? Description, Guid? StatusId)
    : IRequest<TaskResponse?>;
