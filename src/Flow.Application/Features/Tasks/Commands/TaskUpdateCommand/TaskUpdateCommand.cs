using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Tasks.Commands.TaskUpdateCommand;

/// <summary>PATCH-семантика: заполненные поля меняются, null — не трогать.</summary>
public sealed record TaskUpdateCommand(TaskId TaskId, string? Title, string? Description, StatusId? StatusId)
    : IRequest<TaskUpdateResult>;
