using Flow.Application.Abstractions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using Flow.Shared.Contracts.GitIntegration;
using MediatR;
using GitDevelopmentLinkKind = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkKind;
using GitDevelopmentLinkState = Flow.Domain.Entities.GitIntegration.GitDevelopmentLinkState;

namespace Flow.Application.Features.GitIntegration;

// Действия из Flow (docs/TZ_git_integration.md §8, этап 5D): ветка и PR на хостинге прямо из карточки задачи — запись в
// чужую систему от имени токена подключения. Поэтому своё право проекта WriteGit (Developer+), и только в репозиторий,
// привязанный к проекту задачи. Связь (ветка, PR) пишется сразу, не дожидаясь вебхука: он потом лишь обновит её.
// Отказ хостинга (нет прав у токена, ветка уже есть) — 400 с его причиной.

/// <summary>
/// Обработчик - <see cref="GitTaskActionHandlers"/>
/// </summary>
public sealed record TaskGitBranchCreateCommand(Guid ActorId, Guid TaskId, Guid RepositoryId, string Name, string? FromBranch) : IRequest<TaskDevelopmentResponse?>;

/// <summary>
/// Обработчик - <see cref="GitTaskActionHandlers"/>
/// </summary>
public sealed record TaskGitPullRequestCreateCommand(Guid ActorId, Guid TaskId, Guid RepositoryId, string SourceBranch, string? TargetBranch, string? Title, bool Draft)
    : IRequest<TaskDevelopmentResponse?>;

internal sealed class GitTaskActionHandlers(
    IGitHostConnectionRepository connections,
    IGitRepositoryCatalog catalog,
    IGitRepositoryBoardRepository repositoryBoards,
    IGitDevelopmentLinkRepository developmentLinks,
    ITaskItemRepository tasks,
    IGitProviderClient client,
    IGitSecretProtector protector,
    GitOptions options,
    GitAutomation automation,
    ActorResolver actors,
    IPermissionService permissions,
    IProjectAccess projectAccess,
    ISender sender,
    ISearchIndexQueue searchIndex,
    IUnitOfWork unitOfWork) :
    IRequestHandler<TaskGitBranchCreateCommand, TaskDevelopmentResponse?>,
    IRequestHandler<TaskGitPullRequestCreateCommand, TaskDevelopmentResponse?>
{
    public async Task<TaskDevelopmentResponse?> Handle(TaskGitBranchCreateCommand request, CancellationToken cancellationToken)
    {
        if (await PrepareAsync(request.ActorId, request.TaskId, request.RepositoryId, cancellationToken) is not { } ctx)
            return null;

        var name = GitUrls.ValidateBranch(request.Name);
        var from = string.IsNullOrWhiteSpace(request.FromBranch) ? ctx.Repository.DefaultBranch : GitUrls.ValidateBranch(request.FromBranch);
        await CallAsync(() => client.CreateBranchAsync(ctx.Connection, ctx.Token, ctx.Repository, name, from, cancellationToken));

        var link = await LinkAsync(ctx, GitDevelopmentLinkKind.Branch, name, cancellationToken);
        link.Apply(GitUrls.Branch(ctx.Connection.Provider, ctx.Repository.WebUrl, name), name, GitDevelopmentLinkState.Open, null, ctx.Actor.Id, name, null, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sender.Send(new TaskDevelopmentQuery(request.ActorId, request.TaskId), cancellationToken);
    }

    public async Task<TaskDevelopmentResponse?> Handle(TaskGitPullRequestCreateCommand request, CancellationToken cancellationToken)
    {
        if (await PrepareAsync(request.ActorId, request.TaskId, request.RepositoryId, cancellationToken) is not { } ctx)
            return null;

        var source = GitUrls.ValidateBranch(request.SourceBranch);
        var target = string.IsNullOrWhiteSpace(request.TargetBranch) ? ctx.Repository.DefaultBranch : GitUrls.ValidateBranch(request.TargetBranch);
        if (source == target)
            throw new ArgumentException("PR из ветки в неё же не создать.");
        var code = ctx.Task.Code.Value;
        var title = string.IsNullOrWhiteSpace(request.Title) ? $"{code} {ctx.Task.Title}" : request.Title.Trim();
        var body = GitUrls.TaskComment(code, ctx.Task.Title, options.TaskUrl(code));

        GitPullRequest pr = null!;
        await CallAsync(async () => pr = await client.CreatePullRequestAsync(ctx.Connection, ctx.Token, ctx.Repository, source, target, title, body, request.Draft,
            cancellationToken));

        var link = await LinkAsync(ctx, GitDevelopmentLinkKind.PullRequest, pr.Number, cancellationToken);
        var previous = link.Url.Length == 0 ? (GitDevelopmentLinkState?)null : link.State;
        link.Apply(pr.Url, pr.Title, pr.State, pr.AuthorLogin, ctx.Actor.Id, pr.SourceBranch ?? source, pr.TargetBranch ?? target, pr.UpdatedAt);
        searchIndex.Upsert(link, ctx.Task.BoardId);
        // Автопереход «PR открыт» — как если бы PR пришёл вебхуком: через workflow, от имени создавшего.
        await automation.OnPullRequestAsync(ctx.Repository, ctx.Binding, ctx.Task, link, previous, pr, ctx.Actor.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return await sender.Send(new TaskDevelopmentQuery(request.ActorId, request.TaskId), cancellationToken);
    }

    private sealed record Context(User Actor, TaskItem Task, GitRepository Repository, GitRepositoryBoard Binding, GitHostConnection Connection, string Token);

    private async Task<Context?> PrepareAsync(Guid actorId, Guid taskId, Guid repositoryId, CancellationToken cancellationToken)
    {
        var actor = await actors.ResolveAsync(actorId, cancellationToken);
        var task = await tasks.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
            return null;
        permissions.EnsureCanWriteGit(await projectAccess.GetAsync(actor, task.BoardId, cancellationToken));

        var repository = await catalog.GetByIdAsync(repositoryId, cancellationToken);
        var binding = repository is null ? null : (await repositoryBoards.GetAsync(task.BoardId, repository.Id, cancellationToken)).SingleOrDefault();
        if (repository is not { IsActive: true } || binding is null)
            throw new InvalidOperationException("Репозиторий не привязан к проекту задачи или отключён.");
        var connection = await connections.GetByIdAsync(repository.ConnectionId, cancellationToken)
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
        catch (GitProviderException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private async Task<GitDevelopmentLink> LinkAsync(Context ctx, GitDevelopmentLinkKind kind, string externalId, CancellationToken cancellationToken)
    {
        var link = await developmentLinks.FindAsync(ctx.Task.Id, ctx.Repository.Id, kind, externalId, cancellationToken);
        if (link is null)
        {
            link = GitDevelopmentLink.Create(ctx.Task.Id, ctx.Repository.Id, kind, externalId);
            developmentLinks.Add(link);
        }

        return link;
    }
}
