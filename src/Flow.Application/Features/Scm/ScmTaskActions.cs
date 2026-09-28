using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.Scm;
using MediatR;
using ScmLinkKind = Flow.Domain.Entities.GitIntegration.ScmLinkKind;
using ScmLinkState = Flow.Domain.Entities.GitIntegration.ScmLinkState;

namespace Flow.Application.Features.Scm;

// Действия из Flow (docs/TZ_scm_integration.md §8, этап 5D): ветка и PR на хостинге прямо из карточки задачи — запись в
// чужую систему от имени токена подключения. Поэтому своё право проекта WriteScm (Developer+), и только в репозиторий,
// привязанный к проекту задачи. Связь (ветка, PR) пишется сразу, не дожидаясь вебхука: он потом лишь обновит её.
// Отказ хостинга (нет прав у токена, ветка уже есть) — 400 с его причиной.

public sealed record TaskScmBranchCreateCommand(Guid ActorId, Guid TaskId, Guid RepositoryId, string Name, string? FromBranch) : IRequest<TaskDevelopmentResponse?>;

public sealed record TaskScmPullRequestCreateCommand(Guid ActorId, Guid TaskId, Guid RepositoryId, string SourceBranch, string? TargetBranch, string? Title, bool Draft)
    : IRequest<TaskDevelopmentResponse?>;

internal sealed class ScmTaskActionHandlers(
    IScmStore store,
    ITaskItemRepository tasks,
    IScmProviderClient client,
    IScmSecretProtector protector,
    ScmOptions options,
    ScmAutomation automation,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    ISender sender,
    ISearchIndexQueue searchIndex,
    IUnitOfWork unitOfWork) :
    IRequestHandler<TaskScmBranchCreateCommand, TaskDevelopmentResponse?>,
    IRequestHandler<TaskScmPullRequestCreateCommand, TaskDevelopmentResponse?>
{
    public async Task<TaskDevelopmentResponse?> Handle(TaskScmBranchCreateCommand request, CancellationToken cancellationToken)
    {
        if (await PrepareAsync(request.ActorId, request.TaskId, request.RepositoryId, cancellationToken) is not { } ctx)
            return null;

        var name = ScmUrls.ValidateBranch(request.Name);
        var from = string.IsNullOrWhiteSpace(request.FromBranch) ? ctx.Repository.DefaultBranch : ScmUrls.ValidateBranch(request.FromBranch);
        await CallAsync(() => client.CreateBranchAsync(ctx.Connection, ctx.Token, ctx.Repository, name, from, cancellationToken));

        var link = await LinkAsync(ctx, ScmLinkKind.Branch, name, cancellationToken);
        link.Apply(ScmUrls.Branch(ctx.Connection.Provider, ctx.Repository.WebUrl, name), name, ScmLinkState.Open, null, ctx.Actor.Id, name, null, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sender.Send(new TaskDevelopmentQuery(request.ActorId, request.TaskId), cancellationToken);
    }

    public async Task<TaskDevelopmentResponse?> Handle(TaskScmPullRequestCreateCommand request, CancellationToken cancellationToken)
    {
        if (await PrepareAsync(request.ActorId, request.TaskId, request.RepositoryId, cancellationToken) is not { } ctx)
            return null;

        var source = ScmUrls.ValidateBranch(request.SourceBranch);
        var target = string.IsNullOrWhiteSpace(request.TargetBranch) ? ctx.Repository.DefaultBranch : ScmUrls.ValidateBranch(request.TargetBranch);
        if (source == target)
            throw new ArgumentException("PR из ветки в неё же не создать.");
        var code = ctx.Task.Code.Value;
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"{code} {ctx.Task.Title}" : request.Title.Trim();
        var body = ScmUrls.TaskComment(code, ctx.Task.Title, options.TaskUrl(code));

        ScmPullRequest pr = null!;
        await CallAsync(async () => pr = await client.CreatePullRequestAsync(ctx.Connection, ctx.Token, ctx.Repository, source, target, title, body, request.Draft,
            cancellationToken));

        var link = await LinkAsync(ctx, ScmLinkKind.PullRequest, pr.Number, cancellationToken);
        var previous = link.Url.Length == 0 ? (ScmLinkState?)null : link.State;
        link.Apply(pr.Url, pr.Title, pr.State, pr.AuthorLogin, ctx.Actor.Id, pr.SourceBranch ?? source, pr.TargetBranch ?? target, pr.UpdatedAt);
        searchIndex.Upsert(link, ctx.Task.BoardId);
        // Автопереход «PR открыт» — как если бы PR пришёл вебхуком: через workflow, от имени создавшего.
        await automation.OnPullRequestAsync(ctx.Repository, ctx.Binding, ctx.Task, link, previous, pr, ctx.Actor.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sender.Send(new TaskDevelopmentQuery(request.ActorId, request.TaskId), cancellationToken);
    }

    private sealed record Context(User Actor, TaskItem Task, ScmRepository Repository, ScmRepositoryBoard Binding, ScmConnection Connection, string Token);

    private async Task<Context?> PrepareAsync(Guid actorId, Guid taskId, Guid repositoryId, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
            return null;
        permissions.EnsureCanWriteScm(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken));

        var repository = await store.GetRepositoryAsync(repositoryId, cancellationToken);
        var binding = repository is null ? null : (await store.GetBindingsAsync(task.BoardId, repository.Id, cancellationToken)).SingleOrDefault();
        if (repository is not { IsActive: true } || binding is null)
            throw new InvalidOperationException("Репозиторий не привязан к проекту задачи или отключён.");
        var connection = await store.GetConnectionAsync(repository.ConnectionId, cancellationToken)
                         ?? throw new InvalidOperationException("Подключение репозитория не найдено.");
        var token = protector.TryUnprotect(connection.SecretProtected)
                    ?? throw new InvalidOperationException("Токен подключения не расшифровывается — введите его заново в интеграциях.");
        return new Context(actor, task, repository, binding, connection, token);
    }

    private static async Task CallAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (ScmProviderException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private async Task<ScmLink> LinkAsync(Context ctx, ScmLinkKind kind, string externalId, CancellationToken cancellationToken)
    {
        var link = await store.FindLinkAsync(ctx.Task.Id, ctx.Repository.Id, kind, externalId, cancellationToken);
        if (link is null)
        {
            link = ScmLink.Create(ctx.Task.Id, ctx.Repository.Id, kind, externalId);
            store.Add(link);
        }

        return link;
    }
}
