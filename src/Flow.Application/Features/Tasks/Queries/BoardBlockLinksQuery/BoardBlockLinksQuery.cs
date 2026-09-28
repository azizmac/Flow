using Flow.Shared.Contracts.Tasks;
using MediatR;

namespace Flow.Application.Features.Tasks.Queries.BoardBlockLinksQuery;

/// <summary>
/// Связи Blocks внутри проекта — стрелки на роадмапе (docs/TZ_task_views.md §4, этап 2H). Связь с задачей другого
/// проекта сюда не попадает: второй полосы на этом роадмапе нет. null — проект скрыт или его нет (404).
/// </summary>
public sealed record BoardBlockLinksQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<TaskBlockEdge>?>;
