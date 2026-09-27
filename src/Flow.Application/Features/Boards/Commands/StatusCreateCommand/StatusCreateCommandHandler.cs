using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.StatusCreateCommand;

internal sealed class StatusCreateCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<StatusCreateCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(StatusCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        // Новый статус пуст — задач в нём нет, индекс трогать незачем.
        board.AddStatus(request.Name, request.Type, isFinal: request.IsFinal);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
