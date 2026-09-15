using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchStatusQuery;

/// <summary>Диагностика поиска для GET /search/status. Actor нужен: смотреть могут Admin и Owner.</summary>
public sealed record SearchStatusQuery(Guid ActorId) : IRequest<SearchStatusResponse>;
