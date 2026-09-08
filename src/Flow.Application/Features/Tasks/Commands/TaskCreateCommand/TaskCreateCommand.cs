using Flow.Shared.Contracts.Tasks;
using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskCreateCommand;

/// <summary>Response = null, если доска не найдена.</summary>
public sealed record TaskCreateCommand(BoardId BoardId, string Title, string? Description, StatusId? StatusId)
    : IRequest<TaskResponse?>;
