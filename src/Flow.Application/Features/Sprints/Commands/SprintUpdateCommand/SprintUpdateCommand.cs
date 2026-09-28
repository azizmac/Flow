using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintUpdateCommand;

/// <summary>
/// PATCH спринта: null — не трогать; ClearGoal/ClearDates снимают. У завершённого меняется только имя (иначе 400),
/// у активного даты не снимаются. Права — ManageSprints.
/// </summary>
public sealed record SprintUpdateCommand(
    Guid ActorId,
    Guid SprintId,
    string? Name = null,
    string? Goal = null,
    bool ClearGoal = false,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool ClearDates = false) : IRequest<SprintResponse?>;
