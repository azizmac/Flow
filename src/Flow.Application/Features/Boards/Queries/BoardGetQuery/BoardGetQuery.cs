using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

public sealed record BoardGetQuery(Guid ActorId, Guid BoardId) : IRequest<BoardResponse?>;
