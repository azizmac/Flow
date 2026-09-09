using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Application.Features.Boards;
using Flow.Domain.Entities;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Бросает ArgumentException, если Name/Key не проходят валидацию (см. Board.Create).</summary>
internal sealed class BoardCreateCommandHandler(IBoardRepository boards, ActorResolver actors, IPermissionService permissions, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardCreateCommand, BoardCreateResult>
{
    public async Task<BoardCreateResult> Handle(BoardCreateCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        // Board.Create нормализует ключ (trim + upper), поэтому проверка уникальности идёт по board.Key,
        // а не по сырому request.Key: "flw" и "FLW" — одна и та же доска.
        var board = Board.Create(request.Name, request.Key);

        if (await boards.ExistsByKeyAsync(board.Key, cancellationToken))
            return BoardCreateResult.KeyTaken(board.Key);

        boards.Add(board);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Только что созданная доска задач не имеет — счётчик известен без запроса.
        return BoardCreateResult.Success(board.ToResponse(taskCount: 0));
    }
}
