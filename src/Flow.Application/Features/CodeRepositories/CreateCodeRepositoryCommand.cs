using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.CodeRepositories;
using MediatR;

namespace Flow.Application.Features.CodeRepositories;

public sealed record CreateCodeRepositoryCommand(
    Guid ActorId, Guid BoardId, string Provider, string Name, string RemoteUrl, string Branch)
    : IRequest<CodeRepositoryResponse?>;

internal sealed class CreateCodeRepositoryCommandHandler(
    IBoardRepository boards, ActorResolver actors, IPermissionService permissions, IProjectAccess projectAccess, IUnitOfWork unitOfWork)
    : IRequestHandler<CreateCodeRepositoryCommand, CodeRepositoryResponse?>
{
    public async Task<CodeRepositoryResponse?> Handle(CreateCodeRepositoryCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageScm(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        if (!Enum.TryParse<RepositoryProvider>(request.Provider, true, out var provider) ||
            !Enum.IsDefined(provider))
            throw new ArgumentException("Поддерживаются только GitHub и GitLab.", nameof(request.Provider));

        var board = await boards.GetByIdAsync(request.BoardId, cancellationToken);
        if (board is null)
            return null;

        var repository = board.AddCodeRepository(provider, request.Name, request.RemoteUrl, request.Branch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return repository.ToResponse();
    }
}
