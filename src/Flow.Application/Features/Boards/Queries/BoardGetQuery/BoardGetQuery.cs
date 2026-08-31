using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardGetQuery;

public sealed record BoardGetQuery(Guid BoardId) : IRequest<BoardResponse?>;
