using System.Security.Cryptography;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Scm;
using MediatR;
using DomainProvider = Flow.Domain.Entities.ScmProvider;
using DomainState = Flow.Domain.Entities.ScmLinkState;
using DomainKind = Flow.Domain.Entities.ScmLinkKind;
using DomainDeliveryStatus = Flow.Domain.Entities.ScmDeliveryStatus;
using SharedProvider = Flow.Shared.Contracts.Scm.ScmProvider;

namespace Flow.Application.Features.Scm;

// Интеграция с Git-хостингами (docs/TZ_scm_integration.md, этап 5A): подключения и репозитории (глобальные Admin+),
// привязки к проекту (ManageScm), блок «Разработка» задачи (чтение проекта).

public sealed record ScmConnectionListQuery(Guid ActorId) : IRequest<IReadOnlyList<ScmConnectionResponse>>;

public sealed record ScmConnectionCreateCommand(Guid ActorId, SharedProvider Provider, string Name, string Token, string? BaseUrl) : IRequest<ScmConnectionResponse>;

public sealed record ScmConnectionUpdateCommand(Guid ActorId, Guid ConnectionId, string Name, string? BaseUrl, string? Token) : IRequest<ScmConnectionResponse?>;

/// <summary>Удаляет подключение и его репозитории (вебхуки у хостинга — best-effort); связи задач уходят каскадом.</summary>
public sealed record ScmConnectionDeleteCommand(Guid ActorId, Guid ConnectionId) : IRequest<bool>;

public sealed record ScmConnectionCheckCommand(Guid ActorId, Guid ConnectionId) : IRequest<ScmConnectionResponse?>;

public sealed record ScmAvailableRepositoriesQuery(Guid ActorId, Guid ConnectionId, string? Query) : IRequest<IReadOnlyList<ScmRemoteRepositoryResponse>?>;

/// <summary>Добавить репозиторий: создать вебхук с новым секретом. Уже добавленный и выключенный — включается заново.</summary>
public sealed record ScmRepositoryAddCommand(Guid ActorId, Guid ConnectionId, string ExternalId) : IRequest<ScmRepositoryResponse?>;

/// <summary>Отключить репозиторий: вебхук удаляется у хостинга, приём выключается, связи задач остаются.</summary>
public sealed record ScmRepositoryDisableCommand(Guid ActorId, Guid RepositoryId) : IRequest<bool>;

public sealed record ScmBoardRepositoriesQuery(Guid ActorId, Guid BoardId) : IRequest<IReadOnlyList<ScmBoardRepositoryResponse>?>;

public sealed record ScmBindCommand(Guid ActorId, Guid BoardId, Guid RepositoryId, bool Bound) : IRequest<IReadOnlyList<ScmBoardRepositoryResponse>?>;

public sealed record TaskDevelopmentQuery(Guid ActorId, Guid TaskId) : IRequest<TaskDevelopmentResponse?>;

public sealed record ScmDeliveriesQuery(Guid ActorId, Guid RepositoryId, Flow.Shared.Contracts.Scm.ScmDeliveryStatus? Status) : IRequest<IReadOnlyList<ScmDeliveryResponse>?>;

internal static class ScmMapping
{
    public const int CommitPreview = 5;

    public static SharedProvider ToShared(this DomainProvider provider) => (SharedProvider)(int)provider;

    public static ScmRepositoryResponse ToResponse(this ScmRepository r, IEnumerable<ScmRepositoryBoard> bindings) =>
        new(r.Id, r.ConnectionId, r.FullName, r.WebUrl, r.DefaultBranch, r.IsActive, r.WebhookId is not null, r.LastDeliveryAt,
            bindings.Where(b => b.RepositoryId == r.Id).Select(b => b.BoardId).ToList());

    public static ScmLinkResponse ToResponse(this ScmLink l, ScmRepository? repository, DomainProvider provider) =>
        new(l.Id, l.RepositoryId, repository?.FullName ?? "", provider.ToShared(), (Flow.Shared.Contracts.Scm.ScmLinkKind)(int)l.Kind,
            l.ExternalId, l.Url, l.Title, l.State is { } s ? (Flow.Shared.Contracts.Scm.ScmLinkState)(int)s : null, l.AuthorLogin,
            l.AuthorUserId, l.SourceBranch, l.TargetBranch, l.OccurredAt);
}

internal sealed class ScmAdminHandlers(
    IScmStore store,
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
    IRequestHandler<ScmDeliveriesQuery, IReadOnlyList<ScmDeliveryResponse>?>
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
        var repositories = await store.GetRepositoriesAsync(null, cancellationToken);
        var bindings = await store.GetBindingsAsync(null, null, cancellationToken);
        return (await store.GetConnectionsAsync(cancellationToken)).Select(c => Response(c, repositories, bindings)).ToList();
    }

    public async Task<ScmConnectionResponse> Handle(ScmConnectionCreateCommand request, CancellationToken cancellationToken)
    {
        var actor = await AdminAsync(request.ActorId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Token))
            throw new ArgumentException("Нужен токен доступа.", nameof(request.Token));

        var connection = ScmConnection.Create((DomainProvider)(int)request.Provider, request.Name, request.BaseUrl, protector.Protect(request.Token.Trim()), actor.Id);
        await CheckAsync(connection, request.Token.Trim(), cancellationToken);
        store.Add(connection);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Response(connection, [], []);
    }

    public async Task<ScmConnectionResponse?> Handle(ScmConnectionUpdateCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await store.GetConnectionAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = string.IsNullOrWhiteSpace(request.Token) ? null : request.Token.Trim();
        connection.Update(request.Name, request.BaseUrl, token is null ? null : protector.Protect(token));
        if (token is not null)
            await CheckAsync(connection, token, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(connection, cancellationToken);
    }

    public async Task<bool> Handle(ScmConnectionDeleteCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await store.GetConnectionAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return false;

        var token = protector.TryUnprotect(connection.SecretProtected);
        foreach (var repository in await store.GetRepositoriesAsync(connection.Id, cancellationToken))
            await TryDeleteWebhookAsync(connection, token, repository, cancellationToken);

        store.Remove(connection);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ScmConnectionResponse?> Handle(ScmConnectionCheckCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await store.GetConnectionAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        await CheckAsync(connection, protector.TryUnprotect(connection.SecretProtected), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await ResponseAsync(connection, cancellationToken);
    }

    public async Task<IReadOnlyList<ScmRemoteRepositoryResponse>?> Handle(ScmAvailableRepositoriesQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await store.GetConnectionAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = Token(connection);
        var added = (await store.GetRepositoriesAsync(connection.Id, cancellationToken)).Where(r => r.IsActive).Select(r => r.ExternalId).ToHashSet();
        var remote = await Call(() => client.ListRepositoriesAsync(connection, token, request.Query, cancellationToken));
        return remote.Select(r => new ScmRemoteRepositoryResponse(r.ExternalId, r.FullName, r.WebUrl, r.DefaultBranch, added.Contains(r.ExternalId))).ToList();
    }

    public async Task<ScmRepositoryResponse?> Handle(ScmRepositoryAddCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var connection = await store.GetConnectionAsync(request.ConnectionId, cancellationToken);
        if (connection is null)
            return null;

        var token = Token(connection);
        var remote = await Call(() => client.GetRepositoryAsync(connection, token, request.ExternalId, cancellationToken))
                     ?? throw new InvalidOperationException("Репозиторий не найден у хостинга или токену не хватает прав его видеть.");

        // Свежий секрет — и при повторном включении: старый мог утечь вместе с настройками хостинга.
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var repository = await store.FindRepositoryAsync(connection.Id, remote.ExternalId, cancellationToken);
        if (repository is { IsActive: true })
            throw new InvalidOperationException($"Репозиторий {remote.FullName} уже подключён.");
        if (repository is null)
        {
            repository = ScmRepository.Create(connection.Id, remote.ExternalId, remote.FullName, remote.WebUrl, remote.DefaultBranch, protector.Protect(secret));
            store.Add(repository);
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
        var response = repository.ToResponse(await store.GetBindingsAsync(null, repository.Id, cancellationToken));
        // Адрес и секрет для ручной настройки — только если вебхук не создался, и только в этом ответе.
        return repository.WebhookId is null ? response with { ManualWebhookUrl = url, ManualWebhookSecret = secret, WebhookError = webhookError } : response;
    }

    public async Task<bool> Handle(ScmRepositoryDisableCommand request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        var repository = await store.GetRepositoryAsync(request.RepositoryId, cancellationToken);
        if (repository is null)
            return false;

        var connection = (await store.GetConnectionAsync(repository.ConnectionId, cancellationToken))!;
        await TryDeleteWebhookAsync(connection, protector.TryUnprotect(connection.SecretProtected), repository, cancellationToken);
        repository.SetWebhook(null);
        repository.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<ScmDeliveryResponse>?> Handle(ScmDeliveriesQuery request, CancellationToken cancellationToken)
    {
        await AdminAsync(request.ActorId, cancellationToken);
        if (await store.GetRepositoryAsync(request.RepositoryId, cancellationToken) is null)
            return null;

        return (await store.GetDeliveriesAsync(request.RepositoryId, request.Status is { } s ? (DomainDeliveryStatus)(int)s : null, 100, cancellationToken))
            .Select(d => new ScmDeliveryResponse(d.Id, d.DeliveryId, d.Event, d.ReceivedAt, (Flow.Shared.Contracts.Scm.ScmDeliveryStatus)(int)d.Status, d.Attempts, d.LastError))
            .ToList();
    }

    private string Token(ScmConnection connection) =>
        protector.TryUnprotect(connection.SecretProtected)
        ?? throw new InvalidOperationException("Токен подключения не расшифровывается (сменились ключи DataProtection) — введите его заново.");

    private async Task CheckAsync(ScmConnection connection, string? token, CancellationToken cancellationToken)
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

    private async Task TryDeleteWebhookAsync(ScmConnection connection, string? token, ScmRepository repository, CancellationToken cancellationToken)
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

    private async Task<ScmConnectionResponse> ResponseAsync(ScmConnection connection, CancellationToken cancellationToken) =>
        Response(connection, await store.GetRepositoriesAsync(connection.Id, cancellationToken), await store.GetBindingsAsync(null, null, cancellationToken));

    private ScmConnectionResponse Response(ScmConnection c, IEnumerable<ScmRepository> repositories, IReadOnlyList<ScmRepositoryBoard> bindings) =>
        new(c.Id, c.Provider.ToShared(), c.Name, c.BaseUrl, c.CreatedAt, c.LastCheckAt, c.CheckedLogin, c.LastError,
            protector.TryUnprotect(c.SecretProtected) is null,
            repositories.Where(r => r.ConnectionId == c.Id).OrderBy(r => r.FullName).Select(r => r.ToResponse(bindings)).ToList());
}

internal sealed class ScmProjectHandlers(
    IScmStore store,
    ITaskItemRepository tasks,
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
        var repository = await store.GetRepositoryAsync(request.RepositoryId, cancellationToken);
        if (repository is null)
            return null;

        var existing = (await store.GetBindingsAsync(request.BoardId, repository.Id, cancellationToken)).SingleOrDefault();
        if (request.Bound && existing is null)
        {
            if (!repository.IsActive)
                throw new InvalidOperationException("Репозиторий отключён — сначала подключите его заново в интеграциях.");
            store.Add(ScmRepositoryBoard.Create(repository.Id, request.BoardId, actor.Id));
        }
        else if (!request.Bound && existing is not null)
        {
            store.Remove(existing);
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

        var links = await store.GetLinksByTaskAsync(task.Id, cancellationToken);
        var repositories = (await store.GetRepositoriesAsync(null, cancellationToken)).ToDictionary(r => r.Id);
        var providers = (await store.GetConnectionsAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Provider);
        ScmLinkResponse Map(ScmLink l)
        {
            var repository = repositories.GetValueOrDefault(l.RepositoryId);
            return l.ToResponse(repository, repository is null ? DomainProvider.GitHub : providers.GetValueOrDefault(repository.ConnectionId));
        }

        var commits = links.Where(l => l.Kind == DomainKind.Commit).ToList();
        return new TaskDevelopmentResponse(
            links.Where(l => l.Kind == DomainKind.Branch).Select(Map).ToList(),
            links.Where(l => l.Kind == DomainKind.PullRequest).Select(Map).ToList(),
            commits.Take(ScmMapping.CommitPreview).Select(Map).ToList(),
            commits.Count,
            (await store.GetBindingsAsync(task.BoardId, null, cancellationToken)).Count > 0);
    }

    private async Task<IReadOnlyList<ScmBoardRepositoryResponse>> ListAsync(Guid boardId, CancellationToken cancellationToken)
    {
        var bound = (await store.GetBindingsAsync(boardId, null, cancellationToken)).Select(b => b.RepositoryId).ToHashSet();
        var providers = (await store.GetConnectionsAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Provider);
        return (await store.GetRepositoriesAsync(null, cancellationToken))
            .Where(r => r.IsActive || bound.Contains(r.Id))
            .Select(r => new ScmBoardRepositoryResponse(r.Id, providers.GetValueOrDefault(r.ConnectionId).ToShared(), r.FullName, r.WebUrl, bound.Contains(r.Id)))
            .ToList();
    }
}
