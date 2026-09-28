using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.CodeRepositories;
using MediatR;

namespace Flow.Application.Features.CodeRepositories;

public sealed record SynchronizeCodeRepositoryCommand(Guid ActorId, Guid BoardId, Guid RepositoryId)
    : IRequest<CodeRepositoryResponse?>;

internal sealed class SynchronizeCodeRepositoryCommandHandler(
    ICodeRepositoryRepository repositories,
    IRepositoryWorkspaceService workspaces,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SynchronizeCodeRepositoryCommand, CodeRepositoryResponse?>
{
    public async Task<CodeRepositoryResponse?> Handle(SynchronizeCodeRepositoryCommand request, CancellationToken cancellationToken)
    {
        permissions.EnsureCanManageBoards(await actors.ResolveAsync(request.ActorId, cancellationToken));

        var repository = await repositories.GetByIdAsync(request.RepositoryId, cancellationToken);
        if (repository is null || repository.BoardId != request.BoardId)
            return null;

        repository.StartSync();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        try
        {
            var commit = await workspaces.SynchronizeAsync(repository, cancellationToken);
            repository.CompleteSync(commit);
        }
        catch (OperationCanceledException)
        {
            repository.FailSync("Синхронизация прервана.");
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        catch (Exception)
        {
            repository.FailSync("Не удалось получить репозиторий. Проверьте адрес и ветку.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return repository.ToResponse();
    }
}
