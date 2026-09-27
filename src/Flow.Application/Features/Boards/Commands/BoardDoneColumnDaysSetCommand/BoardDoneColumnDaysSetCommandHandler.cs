using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDoneColumnDaysSetCommand;

internal sealed class BoardDoneColumnDaysSetCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDoneColumnDaysSetCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardDoneColumnDaysSetCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        if (board.DoneColumnDays != request.Days)
        {
            board.SetDoneColumnDays(request.Days);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
