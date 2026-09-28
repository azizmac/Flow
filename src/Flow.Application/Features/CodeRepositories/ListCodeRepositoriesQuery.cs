using Flow.Application.Abstractions;
using Flow.Shared.Contracts.CodeRepositories;
using MediatR;

namespace Flow.Application.Features.CodeRepositories;

public sealed record ListCodeRepositoriesQuery(Guid BoardId) : IRequest<IReadOnlyList<CodeRepositoryResponse>?>;

internal sealed class ListCodeRepositoriesQueryHandler(IBoardRepository boards, ICodeRepositoryRepository repositories)
    : IRequestHandler<ListCodeRepositoriesQuery, IReadOnlyList<CodeRepositoryResponse>?>
{
    public async Task<IReadOnlyList<CodeRepositoryResponse>?> Handle(ListCodeRepositoriesQuery request, CancellationToken cancellationToken)
    {
        if (await boards.GetByIdAsync(request.BoardId, cancellationToken) is null)
            return null;

        var items = await repositories.GetByBoardIdAsync(request.BoardId, cancellationToken);
        return items.Select(repository => repository.ToResponse()).ToArray();
    }
}
