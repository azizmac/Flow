using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintCreateCommand;

/// <summary>Новый запланированный спринт в конце очереди проекта; без имени — «Спринт N». Права — ManageSprints.</summary>
public sealed record SprintCreateCommand(Guid ActorId, Guid BoardId, string? Name = null, string? Goal = null, DateOnly? StartDate = null, DateOnly? EndDate = null)
    : IRequest<SprintResponse?>;
