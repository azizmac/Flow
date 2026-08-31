using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Response = null, если доска не найдена.</summary>
public sealed record TaskCreateCommand(Guid BoardId, string Title, string? Description, Guid? StatusId)
    : IRequest<TaskResponse?>;
