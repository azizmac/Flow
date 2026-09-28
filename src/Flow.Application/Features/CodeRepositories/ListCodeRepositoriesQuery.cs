using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Shared.Contracts.CodeRepositories;
using MediatR;

namespace Flow.Application.Features.CodeRepositories;

public sealed record ListCodeRepositoriesQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<CodeRepositoryResponse>?>;

internal sealed class ListCodeRepositoriesQueryHandler(IBoardRepository boards, ICodeRepositoryRepository repositories, ActorResolver actors, IProjectAccess projectAccess)
    : IRequestHandler<ListCodeRepositoriesQuery, IReadOnlyList<CodeRepositoryResponse>?>
{
    public async Task<IReadOnlyList<CodeRepositoryResponse>?> Handle(ListCodeRepositoriesQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null || !(await projectAccess.GetAsync(actor, board.Id, cancellationToken)).CanView)
            return null;

        var items = await repositories.GetByBoardIdAsync(request.BoardId, cancellationToken);
        return items.Select(repository => repository.ToResponse()).ToArray();
    }
}
