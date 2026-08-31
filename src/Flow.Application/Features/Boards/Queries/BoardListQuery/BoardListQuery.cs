using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardListQuery;

public sealed record BoardListQuery : IRequest<IReadOnlyList<BoardResponse>>;
