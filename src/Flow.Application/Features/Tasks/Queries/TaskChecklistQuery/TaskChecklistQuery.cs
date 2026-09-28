using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.TaskChecklistQuery;

/// <summary>Чек-лист задачи по порядку; null — задачи нет или она скрыта (404).</summary>
public sealed record TaskChecklistQuery(Guid ActorId, Guid TaskId) : IRequest<IReadOnlyList<TaskChecklistItemResponse>?>;
