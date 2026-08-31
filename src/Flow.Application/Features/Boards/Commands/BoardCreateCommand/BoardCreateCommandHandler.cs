using Flow.Application.Abstractions;
using Flow.Application.Features.Boards;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Boards;
using MediatR;

namespace Flow.Application.Features.Boards.Commands.BoardCreateCommand;

/// <summary>Бросает ArgumentException, если Name/Key не проходят валидацию (см. Board.Create).</summary>
internal sealed class BoardCreateCommandHandler(IBoardRepository boards, IUnitOfWork unitOfWork)
    : IRequestHandler<BoardCreateCommand, BoardResponse>
{
    public async Task<BoardResponse> Handle(BoardCreateCommand request, CancellationToken cancellationToken)
    {
        var board = Board.Create(request.Name, request.Key);

        boards.Add(board);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return board.ToResponse();
    }
}
