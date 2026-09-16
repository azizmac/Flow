using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SimilarTasksQuery;

/// <summary>
/// Похожие задачи по вектору самой задачи — борьба с дублями в карточке и при создании.
/// Response = null, если поиск выключен или задачи нет (контроллер отвечает 404).
/// </summary>
public sealed record SimilarTasksQuery(Guid ActorId, Guid TaskId, int Limit) : IRequest<IReadOnlyList<SearchResultItem>?>;
