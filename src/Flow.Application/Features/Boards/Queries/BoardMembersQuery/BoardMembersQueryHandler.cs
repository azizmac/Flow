using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMembersQuery;

internal sealed class BoardMembersQueryHandler(IBoardRepository boards, IBoardMemberRepository members, ActorResolver actors)
    : IRequestHandler<BoardMembersQuery, BoardMembersResponse?>
{
    public async Task<BoardMembersResponse?> Handle(BoardMembersQuery request, CancellationToken cancellationToken)
    {
        // Actor пока нужен только для 401 на деактивированного: видеть проект может каждый (приватность — этап 4B).
        await actors.ResolveAsync(request.ActorId, cancellationToken);

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        return board.ToMembersResponse(await members.GetByBoardAsync(board.Id, cancellationToken));
    }
}
