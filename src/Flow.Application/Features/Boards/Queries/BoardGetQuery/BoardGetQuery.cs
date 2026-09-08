using Flow.Shared.Contracts.Boards;
using Flow.Shared.Ids;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

public sealed record BoardGetQuery(BoardId BoardId) : IRequest<BoardResponse?>;
