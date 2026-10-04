using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Commands.SprintStartCommand;

/// <summary>
/// Начать запланированный спринт: даты обязательны, в проекте не должно быть другого активного (400). Пишет снимок
/// обязательств — задачи спринта с их story points. Права — ManageSprints.
/// Обработчик - <see cref="SprintStartCommandHandler"/>
/// </summary>
public sealed record SprintStartCommand(Guid ActorId, Guid SprintId, DateOnly StartDate, DateOnly EndDate) : IRequest<SprintResponse?>;
