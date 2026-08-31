using Flow.Application.Abstractions;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardDeleteCommand;

internal sealed class BoardDeleteCommandHandler(IBoardRepository boards, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardDeleteCommand, bool>
{
    public async Task<bool> Handle(BoardDeleteCommand request, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return false;

        boards.Remove(board);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
