using System.Security.Cryptography;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.Scm;
using MediatR;
using DomainProvider = Flow.Domain.Entities.GitIntegration.GitProvider;
using DomainState = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkState;
using DomainKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using DomainDeliveryStatus = Flow.Domain.Entities.GitIntegration.GitIntegrationJobStatus;
using GitAuthenticationKind = Flow.Domain.Entities.GitIntegration.GitAuthenticationKind;
using SharedProvider = Flow.Shared.Contracts.Scm.GitProvider;
using SharedAuthKind = Flow.Shared.Contracts.Scm.GitAuthenticationKind;

namespace Flow.Application.Features.Scm;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A): подключения и репозитории (глобальные Admin+),
// привязки к проекту (ManageScm), блок «Разработка» задачи (чтение проекта).

public sealed record ScmConnectionListQuery(Guid ActorId) : IRequest<IReadOnlyList<ScmConnectionResponse>>;

/// <summary>Token — токен доступа, у GitHub App — закрытый ключ приложения (PEM); AppId/InstallationId — только у него.</summary>
public sealed record ScmConnectionCreateCommand(Guid ActorId, SharedProvider Provider, string Name, string Token, string? BaseUrl,
    SharedAuthKind AuthKind = SharedAuthKind.Token, long? AppId = null, long? InstallationId = null) : IRequest<ScmConnectionResponse>;

public sealed record ScmConnectionUpdateCommand(Guid ActorId, Guid ConnectionId, string Name, string? BaseUrl, string? Token,
    long? AppId = null, long? InstallationId = null) : IRequest<ScmConnectionResponse?>;

/// <summary>Удаляет подключение и его репозитории (вебхуки у хостинга — best-effort); связи задач уходят каскадом.</summary>
public sealed record ScmConnectionDeleteCommand(Guid ActorId, Guid ConnectionId) : IRequest<bool>;

public sealed record ScmConnectionCheckCommand(Guid ActorId, Guid ConnectionId) : IRequest<ScmConnectionResponse?>;

public sealed record ScmAvailableRepositoriesQuery(Guid ActorId, Guid ConnectionId, string? Query) : IRequest<IReadOnlyList<ScmRemoteRepositoryResponse>?>;

/// <summary>Добавить репозиторий: создать вебхук с новым секретом. Уже добавленный и выключенный — включается заново.</summary>
public sealed record ScmRepositoryAddCommand(Guid ActorId, Guid ConnectionId, string ExternalId) : IRequest<ScmRepositoryResponse?>;

/// <summary>Отключить репозиторий: вебхук удаляется у хостинга, приём выключается, связи задач остаются.</summary>
public sealed record ScmRepositoryDisableCommand(Guid ActorId, Guid RepositoryId) : IRequest<bool>;

public sealed record ScmBoardRepositoriesQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<ScmBoardRepositoryResponse>?>;

/// <summary>Привязать/отвязать; Settings — автоматизация привязки (этап 5C), null — не менять.</summary>
public sealed record ScmBindCommand(Guid ActorId, Guid BoardId, Guid RepositoryId, bool Bound, UpdateScmBindingRequest? Settings = null)
    : IRequest<IReadOnlyList<ScmBoardRepositoryResponse>?>;

public sealed record TaskDevelopmentQuery(Guid ActorId, Guid TaskId) : IRequest<TaskDevelopmentResponse?>;

public sealed record ScmDeliveriesQuery(Guid ActorId, Guid RepositoryId, Flow.Shared.Contracts.Scm.GitIntegrationJobStatus? Status) : IRequest<IReadOnlyList<ScmDeliveryResponse>?>;

/// <summary>Повторить доставку с ошибкой (диагностика, этап 5B): снова в очередь, попытки с нуля. null — нет такой.</summary>
public sealed record ScmDeliveryRetryCommand(Guid ActorId, Guid DeliveryId) : IRequest<ScmDeliveryResponse?>;

/// <summary>
/// Дозагрузить историю репозитория вручную (этап 5B). Задание ставится в очередь доставок; уже стоящее — не дублируется.
/// false — репозитория нет.
/// </summary>
public sealed record ScmBackfillCommand(Guid ActorId, Guid RepositoryId) : IRequest<bool>;

internal static class ScmMapping
{
    public const int CommitPreview = 5;

    public static SharedProvider ToShared(this DomainProvider provider) => (SharedProvider)(int)provider;

    public static ScmRepositoryResponse ToResponse(this GitRepository r, IEnumerable<GitRepositoryBoard> bindings, IReadOnlyDictionary<Guid, int>? failed = null) =>
        new(r.Id, r.ConnectionId, r.FullName, r.WebUrl, r.DefaultBranch, r.IsActive, r.WebhookId is not null, r.LastDeliveryAt,
            bindings.Where(b => b.RepositoryId == r.Id).Select(b => b.BoardId).ToList(), FailedDeliveries: failed?.GetValueOrDefault(r.Id) ?? 0);

    public static ScmDeliveryResponse ToResponse(this GitIntegrationJob d) =>
        new(d.Id, d.DeliveryId, d.Event, d.ReceivedAt, (Flow.Shared.Contracts.Scm.GitIntegrationJobStatus)(int)d.Status, d.Attempts, d.LastError,
            d.Status == DomainDeliveryStatus.Pending ? d.NextAttemptAt : null, d.IsBackfill);

    /// <summary>Поставить дозагрузку истории, если такой ещё нет в очереди.</summary>
    public static async Task EnqueueBackfillAsync(this IGitIntegrationJobRepository jobs, Guid repositoryId, CancellationToken cancellationToken)
    {
        if (!await jobs.HasPendingAsync(repositoryId, GitIntegrationJob.BackfillEvent, cancellationToken))
            jobs.Add(GitIntegrationJob.CreateBackfill(repositoryId));
    }

    public static ScmLinkResponse ToResponse(this GitDevelopmentLink l, GitRepository? repository, DomainProvider provider) =>
        new(l.Id, l.RepositoryId, repository?.FullName ?? "", provider.ToShared(), (Flow.Shared.Contracts.Scm.GitDevelopmentLinkKind)(int)l.Kind,
            l.ExternalId, l.Url, l.Title, l.State is { } s ? (Flow.Shared.Contracts.Scm.GitDevelopmentLinkState)(int)s : null, l.AuthorLogin,
            l.AuthorUserId, l.SourceBranch, l.TargetBranch, l.OccurredAt, l.Note);
}

internal sealed class ScmAdminHandlers(
    IGitHostConnectionRepository connections,
    IGitRepositoryCatalog catalog,
    IGitRepositoryBoardRepository repositoryBoards,
    IGitIntegrationJobRepository jobs,
    IScmProviderClient client,
    IScmSecretProtector protector,
    ScmOptions options,
    ActorResolver actors,
    IPermissionService permissions,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ScmConnectionListQuery, IReadOnlyList<ScmConnectionResponse>>,
    IRequestHandler<ScmConnectionCreateCommand, ScmConnectionResponse>,
    IRequestHandler<ScmConnectionUpdateCommand, ScmConnectionResponse?>,
    IRequestHandler<ScmConnectionDeleteCommand, bool>,
    IRequestHandler<ScmConnectionCheckCommand, ScmConnectionResponse?>,
    IRequestHandler<ScmAvailableRepositoriesQuery, IReadOnlyList<ScmRemoteRepositoryResponse>?>,
    IRequestHandler<ScmRepositoryAddCommand, ScmRepositoryResponse?>,
    IRequestHandler<ScmRepositoryDisableCommand, bool>,
    IRequestHandler<ScmDeliveriesQuery, IReadOnlyList<ScmDeliveryResponse>?>,
    IRequestHandler<ScmDeliveryRetryCommand, ScmDeliveryResponse?>,
    IRequestHandler<ScmBackfillCommand, bool>
{
    private async Task<User> AdminAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        permissions.EnsureCanManageIntegrations(actor);
        return actor;
    }

    public async Task<IReadOnlyList<ScmConnectionResponse>> Handle(ScmConnectionListQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var repositories = await catalog.GetAllAsync(null, cancellationToken);
        var bindings = await repositoryBoards.GetAsync(null, null, cancellationToken);
        var failed = await jobs.GetFailedCountsAsync(cancellationToken);
        return (await connections.GetAllAsync(cancellationToken)).Select(c => Response(c, repositories, bindings, failed)).ToList();
    }

    public async Task<ScmConnectionResponse> Handle(ScmConnectionCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ArgumentException("Нужен токен доступа.", nameof(request.Token));

        var secret = Secret(request.Token);
        var connection = GitHostConnection.Create((DomainProvider)(int)request.Provider, request.Name, request.BaseUrl, protector.Protect(secret), actor.Id,
            (GitAuthenticationKind)(int)request.AuthKind, request.AppId, request.InstallationId);
        await CheckAsync(connection, secret, cancellationToken);
        connections.Add(connection);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Response(connection, [], [], new Dictionary<Guid, int>());
    }

    public async Task<ScmConnectionResponse?> Handle(ScmConnectionUpdateCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await connections.GetByIdAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = string.IsNullOrWhiteSpace(request.Token) ? null : Secret(request.Token);
        var appChanged = connection.AuthKind == GitAuthenticationKind.GitHubApp && (request.AppId is not null || request.InstallationId is not null)
                                                                      && (request.AppId ?? connection.AppId, request.InstallationId ?? connection.InstallationId) != (connection.AppId, connection.InstallationId);
        connection.Update(request.Name, request.BaseUrl, token is null ? null : protector.Protect(token));
        if (appChanged)
            connection.SetApp(request.AppId ?? connection.AppId, request.InstallationId ?? connection.InstallationId);
        if (token is not null || appChanged)
            await CheckAsync(connection, token ?? protector.TryUnprotect(connection.SecretProtected), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(connection, cancellationToken);
    }

    public async Task<bool> Handle(ScmConnectionDeleteCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await connections.GetByIdAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return false;

        var token = protector.TryUnprotect(connection.SecretProtected);
        foreach (var repository in await catalog.GetAllAsync(connection.Id, cancellationToken))
            await TryDeleteWebhookAsync(connection, token, repository, cancellationToken);

        connections.Remove(connection);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ScmConnectionResponse?> Handle(ScmConnectionCheckCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await connections.GetByIdAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        await CheckAsync(connection, protector.TryUnprotect(connection.SecretProtected), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<ScmRemoteRepositoryResponse>?> Handle(ScmAvailableRepositoriesQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await connections.GetByIdAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = Token(connection);
        var added = (await catalog.GetAllAsync(connection.Id, cancellationToken)).Where(r => r.IsActive).Select(r => r.ExternalId).ToHashSet();
        var remote = await Call(() => client.ListRepositoriesAsync(connection, token, request.Query, cancellationToken));
        return remote.Select(r => new ScmRemoteRepositoryResponse(r.ExternalId, r.FullName, r.WebUrl, r.DefaultBranch, added.Contains(r.ExternalId))).ToList();
    }

    public async Task<ScmRepositoryResponse?> Handle(ScmRepositoryAddCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await connections.GetByIdAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = Token(connection);
        var remote = await Call(() => client.GetRepositoryAsync(connection, token, request.ExternalId, cancellationToken))
                     ?? throw new InvalidOperationException("Репозиторий не найден у хостинга или токену не хватает прав его видеть.");

        // Свежий секрет — и при повторном включении: старый мог утечь вместе с настройками хостинга.
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var repository = await catalog.FindAsync(connection.Id, remote.ExternalId, cancellationToken);
        if (repository is { IsActive: true })
            throw new InvalidOperationException($"Репозиторий {remote.FullName} уже подключён.");
        if (repository is null)
        {
            repository = GitRepository.Create(connection.Id, remote.ExternalId, remote.FullName, remote.WebUrl, remote.DefaultBranch, protector.Protect(secret));
            catalog.Add(repository);
        }
        else
        {
            await TryDeleteWebhookAsync(connection, token, repository, cancellationToken);
            repository.Reactivate(protector.Protect(secret));
        }

        repository.SetDefaultBranch(remote.DefaultBranch);
        string? webhookError = null;
        var url = options.WebhookUrl(repository.Id);
        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            webhookError = "Не задан Scm:PublicBaseUrl — хостинг не знает, куда слать события. Настройте вебхук вручную.";
            repository.SetWebhook(null);
        }
        else
        {
            try
            {
                repository.SetWebhook(await client.CreateWebhookAsync(connection, token, repository, url, secret, cancellationToken));
            }
            catch (ScmProviderException ex)
            {
                webhookError = $"Вебхук не создан: {ex.Message} Настройте его вручную.";
                repository.SetWebhook(null);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        var response = repository.ToResponse(await repositoryBoards.GetAsync(null, repository.Id, cancellationToken));
        // Адрес и секрет для ручной настройки — только если вебхук не создался, и только в этом ответе.
        return repository.WebhookId is null ? response with { ManualWebhookUrl = url, ManualWebhookSecret = secret, WebhookError = webhookError } : response;
    }

    public async Task<bool> Handle(ScmRepositoryDisableCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var repository = await catalog.GetByIdAsync(request.RepositoryId, cancellationToken);
        if (repository is null)
            return false;

        var connection = (await connections.GetByIdAsync(repository.ConnectionId, cancellationToken))!;
        await TryDeleteWebhookAsync(connection, protector.TryUnprotect(connection.SecretProtected), repository, cancellationToken);
        repository.SetWebhook(null);
        repository.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ScmDeliveryResponse>?> Handle(ScmDeliveriesQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        if (await catalog.GetByIdAsync(request.RepositoryId, cancellationToken) is null)
            return null;

        return (await jobs.GetByRepositoryIdAsync(request.RepositoryId, request.Status is { } s ? (DomainDeliveryStatus)(int)s : null, 100, cancellationToken))
            .Select(d => d.ToResponse())
            .ToList();
    }

    public async Task<ScmDeliveryResponse?> Handle(ScmDeliveryRetryCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        if (await jobs.GetByIdAsync(request.DeliveryId, cancellationToken) is not { } delivery)
            return null;

        delivery.Retry(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return delivery.ToResponse();
    }

    public async Task<bool> Handle(ScmBackfillCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        if (await catalog.GetByIdAsync(request.RepositoryId, cancellationToken) is not { } repository)
            return false;
        if (!repository.IsActive)
            throw new InvalidOperationException("Репозиторий отключён — сначала подключите его заново.");

        await jobs.EnqueueBackfillAsync(repository.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Токен — без пробелов по краям; закрытый ключ PEM — тоже, но переводы строк внутри него нужны.</summary>
    private static string Secret(string? value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Нужен токен доступа или закрытый ключ приложения.", nameof(value)) : value.Trim();

    private string Token(GitHostConnection connection) =>
        protector.TryUnprotect(connection.SecretProtected)
        ?? throw new InvalidOperationException("Токен подключения не расшифровывается (сменились ключи DataProtection) — введите его заново.");

    private async Task CheckAsync(GitHostConnection connection, string? token, CancellationToken cancellationToken)
    {
        if (token is null)
        {
            connection.MarkChecked(null, "Токен не расшифровывается (сменились ключи DataProtection) — введите его заново.", DateTime.UtcNow);
            return;
        }

        try
        {
            connection.MarkChecked(await client.CheckAsync(connection, token, cancellationToken), null, DateTime.UtcNow);
        }
        catch (ScmProviderException ex)
        {
            connection.MarkChecked(null, ex.Message, DateTime.UtcNow);
        }
    }

    private static async Task<T> Call<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ScmProviderException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private async Task TryDeleteWebhookAsync(GitHostConnection connection, string? token, GitRepository repository, CancellationToken cancellationToken)
    {
        if (token is null || repository.WebhookId is null)
            return;
        try
        {
            await client.DeleteWebhookAsync(connection, token, repository, repository.WebhookId, cancellationToken);
        }
        catch (ScmProviderException)
        {
            // Best-effort: вебхук останется у хостинга, но Flow его доставки больше не примет (репозиторий выключен).
        }
    }

    private async Task<ScmConnectionResponse> ResponseAsync(GitHostConnection connection, CancellationToken cancellationToken) =>
        Response(connection, await catalog.GetAllAsync(connection.Id, cancellationToken), await repositoryBoards.GetAsync(null, null, cancellationToken),
            await jobs.GetFailedCountsAsync(cancellationToken));

    private ScmConnectionResponse Response(GitHostConnection c, IEnumerable<GitRepository> repositories, IReadOnlyList<GitRepositoryBoard> bindings,
        IReadOnlyDictionary<Guid, int> failed) =>
        new(c.Id, c.Provider.ToShared(), c.Name, c.BaseUrl, c.CreatedAt, c.LastCheckAt, c.CheckedLogin, c.LastError,
            protector.TryUnprotect(c.SecretProtected) is null,
            repositories.Where(r => r.ConnectionId == c.Id).OrderBy(r => r.FullName).Select(r => r.ToResponse(bindings, failed)).ToList(),
            (SharedAuthKind)(int)c.AuthKind, c.AppId, c.InstallationId);
}

internal sealed class ScmProjectHandlers(
    IGitHostConnectionRepository connections,
    IGitRepositoryCatalog catalog,
    IGitRepositoryBoardRepository repositoryBoards,
    IGitDevelopmentLinkRepository developmentLinks,
    IGitIntegrationJobRepository jobs,
    ITaskItemRepository tasks,
    IBoardRepository boards,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    IUnitOfWork unitOfWork) :
    IRequestHandler<ScmBoardRepositoriesQuery, IReadOnlyList<ScmBoardRepositoryResponse>?>,
    IRequestHandler<ScmBindCommand, IReadOnlyList<ScmBoardRepositoryResponse>?>,
    IRequestHandler<TaskDevelopmentQuery, TaskDevelopmentResponse?>
{
    public async Task<IReadOnlyList<ScmBoardRepositoryResponse>?> Handle(ScmBoardRepositoriesQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageScm(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        return await ListAsync(request.BoardId, cancellationToken);
    }

    public async Task<IReadOnlyList<ScmBoardRepositoryResponse>?> Handle(ScmBindCommand request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        permissions.EnsureCanManageScm(await projectAccess.GetAsync(actor, request.BoardId, cancellationToken));
        var repository = await catalog.GetByIdAsync(request.RepositoryId, cancellationToken);
        if (repository is null)
            return null;

        var existing = (await repositoryBoards.GetAsync(request.BoardId, repository.Id, cancellationToken)).SingleOrDefault();
        if (request.Bound && existing is null)
        {
            if (!repository.IsActive)
                throw new InvalidOperationException("Репозиторий отключён — сначала подключите его заново в интеграциях.");
            existing = GitRepositoryBoard.Create(repository.Id, request.BoardId, actor.Id);
            repositoryBoards.Add(existing);
            // Задачи проекта уже упоминались в PR и коммитах до привязки — их подтянет дозагрузка истории (этап 5B).
            await jobs.EnqueueBackfillAsync(repository.Id, cancellationToken);
        }

        if (request.Bound && request.Settings is { } settings)
        {
            var board = await boards.GetByIdAsync(request.BoardId, cancellationToken)
                        ?? throw new InvalidOperationException("Проект не найден.");
            foreach (var statusId in new[] { settings.OnPullRequestOpenedStatusId, settings.OnPullRequestMergedStatusId }.OfType<Guid>())
                if (board.Statuses.All(s => s.Id != statusId))
                    throw new ArgumentException("Статус автоперехода — не из этого проекта.", nameof(request.Settings));
            existing!.Configure(settings.OnPullRequestOpenedStatusId, settings.OnPullRequestMergedStatusId, settings.SmartCommits, settings.CommentOnPullRequests);
        }
        else if (!request.Bound && existing is not null)
        {
            repositoryBoards.Remove(existing);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ListAsync(request.BoardId, cancellationToken);
    }

    public async Task<TaskDevelopmentResponse?> Handle(TaskDevelopmentQuery request, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(request.ActorId, cancellationToken);
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null || !(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).CanView)
            return null;

        var links = await developmentLinks.GetByTaskIdAsync(task.Id, cancellationToken);
        var repositories = (await catalog.GetAllAsync(null, cancellationToken)).ToDictionary(r => r.Id);
        var providers = (await connections.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Provider);
        ScmLinkResponse Map(GitDevelopmentLink l)
        {
            var repository = repositories.GetValueOrDefault(l.RepositoryId);
            return l.ToResponse(repository, repository is null ? DomainProvider.GitHub : providers.GetValueOrDefault(repository.ConnectionId));
        }

        var commits = links.Where(l => l.Kind == DomainKind.Commit).ToList();
        var bindings = await repositoryBoards.GetAsync(task.BoardId, null, cancellationToken);
        var targets = bindings.Select(b => repositories.GetValueOrDefault(b.RepositoryId)).OfType<GitRepository>().Where(r => r.IsActive)
            .Select(r => new ScmTaskRepositoryResponse(r.Id, r.FullName, r.DefaultBranch, providers.GetValueOrDefault(r.ConnectionId).ToShared()))
            .ToList();
        return new TaskDevelopmentResponse(
            links.Where(l => l.Kind == DomainKind.Branch).Select(Map).ToList(),
            links.Where(l => l.Kind == DomainKind.PullRequest).Select(Map).ToList(),
            commits.Take(ScmMapping.CommitPreview).Select(Map).ToList(),
            commits.Count,
            bindings.Count > 0,
            targets,
            (await projectAccess.GetAsync(actor, task.BoardId, cancellationToken)).Has(ProjectPermission.WriteScm));
    }

    private async Task<IReadOnlyList<ScmBoardRepositoryResponse>> ListAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var bound = (await repositoryBoards.GetAsync(boardId, null, cancellationToken)).ToDictionary(b => b.RepositoryId);
        var providers = (await connections.GetAllAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Provider);
        return (await catalog.GetAllAsync(null, cancellationToken))
            .Where(r => r.IsActive || bound.ContainsKey(r.Id))
            .Select(r => bound.GetValueOrDefault(r.Id) is { } b
                ? new ScmBoardRepositoryResponse(r.Id, providers.GetValueOrDefault(r.ConnectionId).ToShared(), r.FullName, r.WebUrl, true,
                    b.OnPullRequestOpenedStatusId, b.OnPullRequestMergedStatusId, b.SmartCommits, b.CommentOnPullRequests,
                    r.DefaultBranch, r.SyncState.ToString(), r.LastSyncedCommit, r.LastSyncedAt, r.LastSyncError)
                : new ScmBoardRepositoryResponse(r.Id, providers.GetValueOrDefault(r.ConnectionId).ToShared(), r.FullName, r.WebUrl, false,
                    DefaultBranch: r.DefaultBranch, SyncState: r.SyncState.ToString(), LastSyncedCommit: r.LastSyncedCommit,
                    LastSyncedAt: r.LastSyncedAt, LastSyncError: r.LastSyncError))
            .ToList();
    }
}
