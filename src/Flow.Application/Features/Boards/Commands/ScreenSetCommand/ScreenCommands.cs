using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;
using DomainContext = Flow.Domain.Entities.ScreenContext;

namespace Flow.Application.Features.Boards.Commands.ScreenSetCommand;

/// <summary>
/// Экран задач проекта (docs/TZ_workflow_config.md §3) заменяется целиком: TaskTypeId = null — для всех типов.
/// Права — ManageConfig; ответ — проект целиком; чужой тип, неизвестное поле, дубль, поле, которого нет в форме
/// создания, на Create — 400.
/// Обработчик - <see cref="ScreenCommandHandlers"/>
/// </summary>
public sealed record ScreenSetCommand(Guid ActorId, Guid BoardId, Guid? TaskTypeId, DomainContext Context, IReadOnlyList<ScreenFieldDto> Fields)
    : IRequest<BoardResponse?>;

/// <summary>
/// Сбросить экран к «для всех типов» или встроенному. Права — ManageConfig.
/// Обработчик - <see cref="ScreenCommandHandlers"/>
/// </summary>
public sealed record ScreenResetCommand(Guid ActorId, Guid BoardId, Guid? TaskTypeId, DomainContext Context) : IRequest<BoardResponse?>;

internal sealed class ScreenCommandHandlers(
    IBoardRepository boards,
    ITaskItemRepository tasks,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ScreenSetCommand, BoardResponse?>, IRequestHandler<ScreenResetCommand, BoardResponse?>
{
    public Task<BoardResponse?> Handle(ScreenSetCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.BoardId, board => board.SetScreen(request.TaskTypeId, request.Context,
            request.Fields.Select(f => new ScreenField(f.Field, f.Required, f.Section)).ToList()), cancellationToken);

    public Task<BoardResponse?> Handle(ScreenResetCommand request, CancellationToken cancellationToken) =>
        ChangeAsync(request.ActorId, request.BoardId, board => board.ResetScreen(request.TaskTypeId, request.Context), cancellationToken);

    private async Task<BoardResponse?> ChangeAsync(Guid actorId, Guid boardId, Action<Board> change, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        permissions.EnsureCanManageConfig(await projectAccess.GetAsync(actor, boardId, cancellationToken));

        var board = await boards.GetByIdAsync(boardId, cancellationToken);
        if (board is null)
            return null;

        change(board);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await tasks.CountByBoardIdsAsync([board.Id], cancellationToken);
        return board.ToResponse(counts.GetValueOrDefault(board.Id));
    }
}
