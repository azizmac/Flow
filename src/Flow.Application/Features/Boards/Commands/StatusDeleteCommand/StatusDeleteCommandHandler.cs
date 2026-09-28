using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.StatusDeleteCommand;

internal sealed class StatusDeleteCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ITaskActivityRepository activities,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<StatusDeleteCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(StatusDeleteCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        // Сначала домен: он проверяет инварианты (начальный, последний финальный, целевой статус из проекта)
        // до того, как хоть одна задача сдвинется. Задачи переезжают в той же транзакции: FK TaskItems.StatusId —
        // Restrict, и EF отправит их UPDATE раньше DELETE статуса.
        board.RemoveStatus(request.StatusId, request.MoveToStatusId);

        // Служебный перенос — без проверки прав на каждую задачу: право настраивать проект его и подразумевает.
        foreach (var task in await tasks.GetByStatusIdAsync(request.StatusId, cancellationToken))
        {
            activities.Add(TaskActivity.StatusChanged(task.Id, actor.Id, task.StatusId, request.MoveToStatusId));
            task.ChangeStatus(request.MoveToStatusId);
            // Финальность нового статуса может отличаться — IsClosed чанков обновит воркер.
            searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
