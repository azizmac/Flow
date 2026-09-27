using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.TaskTypeUpdateCommand;

internal sealed class TaskTypeUpdateCommandHandler(IBoardRepository boards, ITaskItemRepository tasks, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<TaskTypeUpdateCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(TaskTypeUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var access = await projectAccess.GetAsync(actor, request.BoardId, cancellationToken);
        permissions.EnsureCanManageConfig(access);

        if (request.IsDefault == false)
            throw new ArgumentException("Default flag can only be moved to another type, not cleared.", nameof(request.IsDefault));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        if (request.Name is not null)
            board.RenameTaskType(request.TypeId, request.Name);

        // Порядок важен: «вернуть из архива и сделать основным» одним запросом должно работать,
        // а «сделать основным и архивировать» — упереться в доменную проверку.
        if (request.IsArchived == false)
            board.SetTaskTypeArchived(request.TypeId, false);

        if (request.IsDefault == true)
            board.SetDefaultTaskType(request.TypeId);

        if (request.IsArchived == true)
            board.SetTaskTypeArchived(request.TypeId, true);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
