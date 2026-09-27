using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.TaskTypeCreateCommand;

internal sealed class TaskTypeCreateCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskTypeCreateCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(TaskTypeCreateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        board.AddTaskType(request.Name, request.Kind, request.IsDefault);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
