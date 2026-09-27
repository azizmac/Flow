using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskLinkListQuery;

/// <summary>Связи задачи обоих направлений; null — задачи нет или она скрыта (404).</summary>
public sealed record TaskLinkListQuery(Guid ActorId, Guid TaskId) : IRequest<IReadOnlyList<TaskLinkResponse>?>;
