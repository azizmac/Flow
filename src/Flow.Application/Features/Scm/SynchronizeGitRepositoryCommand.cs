using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities.GitIntegration;
using MediatR;

namespace Flow.Application.Features.Scm;

public sealed record SynchronizeGitRepositoryCommand(Guid ActorId, Guid BoardId, Guid RepositoryId)
    : IRequest<bool?>;

internal sealed class SynchronizeGitRepositoryCommandHandler(
    IScmStore store,
    IRepositoryWorkspaceService workspaces,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork)
    : IRequestHandler<SynchronizeGitRepositoryCommand, bool?>
{
    public async Task<bool?> Handle(SynchronizeGitRepositoryCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageScm(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));

        var repository = await store.GetRepositoryAsync(request.RepositoryId, cancellationToken);
        if (repository is null || !(await store.GetBindingsAsync(request.BoardId, request.RepositoryId, cancellationToken)).Any())
            return null;

        var connection = await store.GetConnectionAsync(repository.ConnectionId, cancellationToken);
        if (connection is null || !CanClonePublicRepository(connection.Provider, repository.WebUrl))
            throw new InvalidOperationException("Локальная синхронизация пока доступна только для публичных репозиториев GitHub и GitLab.");

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
        return true;
    }

    private static bool CanClonePublicRepository(GitProvider provider, string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        string.Equals(uri.Host, provider switch
        {
            GitProvider.GitHub => "github.com",
            GitProvider.GitLab => "gitlab.com",
            _ => null
        }, StringComparison.OrdinalIgnoreCase);
}
