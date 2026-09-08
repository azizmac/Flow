using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardRenameCommand;

/// <summary>Бросает ArgumentException при пустом названии (см. Board.Rename).</summary>
internal sealed class BoardRenameCommandHandler(IBoardRepository boards, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardRenameCommand, BoardResponse?>
{
    public async Task<BoardResponse?> Handle(BoardRenameCommand request, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        board.Rename(request.Name);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return board.ToResponse();
    }
}
