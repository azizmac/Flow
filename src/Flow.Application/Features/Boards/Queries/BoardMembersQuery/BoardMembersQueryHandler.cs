using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Queries.BoardMembersQuery;

internal sealed class BoardMembersQueryHandler(IBoardRepository boards, IBoardMemberRepository members, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<BoardMembersQuery, BoardMembersResponse?>
{
    public async Task<BoardMembersResponse?> Handle(BoardMembersQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);

        // Участников видит всякий, кто видит проект; скрытый проект — как несуществующий.
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null || !(await projectAccess.GetAsync(actor, board.Id, cancellationToken)).CanView)
            return null;

        return await members.MembersResponseAsync(board, cancellationToken);
    }
}
