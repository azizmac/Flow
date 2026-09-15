using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Search.Queries.SearchStatusQuery;

/// <summary>Состояние поискового индекса. ActorId — Admin и выше (403 остальным).</summary>
public sealed record SearchStatusQuery(Guid ActorId) : IRequest<SearchStatusResponse>;
