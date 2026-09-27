using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.Boards;
using Flow.Shared.Contracts.Search;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.StatusUpdateCommand;

internal sealed class StatusUpdateCommandHandler(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ISearchIndexQueue searchIndex,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<StatusUpdateCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(StatusUpdateCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        if (request.IsInitial == false)
            throw new ArgumentException("Initial flag can only be moved to another status, not cleared.", nameof(request.IsInitial));
        if (request.ClearType && request.Type is not null)
            throw new ArgumentException("Type and ClearType are mutually exclusive.", nameof(request.ClearType));
        if (request.ClearWipLimit && request.WipLimit is not null)
            throw new ArgumentException("WipLimit and ClearWipLimit are mutually exclusive.", nameof(request.ClearWipLimit));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var status = board.GetStatus(request.StatusId);

        if (request.Name is not null)
            board.RenameStatus(status.Id, request.Name);

        if (request.Type is not null || request.ClearType)
            board.SetStatusType(status.Id, request.Type);

        if (request.WipLimit is not null || request.ClearWipLimit)
            board.SetStatusWipLimit(status.Id, request.WipLimit);

        // Порядок как у типов задач: «снять финальность и сделать начальным» одним запросом должно работать.
        var wasFinal = status.IsFinal;
        if (request.IsFinal == false)
            board.SetStatusFinal(status.Id, false);

        if (request.IsInitial == true)
            board.SetInitialStatus(status.Id);

        if (request.IsFinal == true)
            board.SetStatusFinal(status.Id, true);

        // Финальность статуса — это IsClosed у чанков его задач: без переиндексации закрытые задачи выпадали бы
        // из выдачи по умолчанию уже после того, как статус перестал их закрывать (и наоборот). Текст не меняется,
        // поэтому воркер обновит только флаг, без реэмбеддинга.
        if (status.IsFinal != wasFinal)
        {
            foreach (var task in await tasks.GetByStatusIdAsync(status.Id, cancellationToken))
                searchIndex.Enqueue(SearchSourceType.Task, task.Id, board.Id, SearchIndexOperation.Upsert);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
