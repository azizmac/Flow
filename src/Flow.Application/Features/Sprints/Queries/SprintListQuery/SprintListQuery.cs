using Flow.Shared.Contracts.Sprints;
using MediatR;

namespace Flow.Application.Features.Sprints.Queries.SprintListQuery;

/// <summary>Спринты проекта: активный, запланированные, завершённые (от новых). null — проект скрыт или его нет.</summary>
public sealed record SprintListQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<SprintResponse>?>;
