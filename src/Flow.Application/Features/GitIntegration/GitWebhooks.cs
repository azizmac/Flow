using System.Security.Cryptography;
using System.Text.Json;
using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Domain.Entities.GitIntegration;
using MediatR;

namespace Flow.Application.Features.GitIntegration;

public enum GitWebhookResult { NotFound, Unauthorized, BadRequest, Duplicate, Accepted, Ignored }

/// <summary>
/// Приём вебхука (docs/TZ_git_integration.md §2): подпись по сырому телу до разбора JSON, повтор доставки — Duplicate
/// без повторной обработки, событие нормализуется сразу и пишется в очередь; разбор — воркером. Заголовки —
/// без учёта регистра.
/// Обработчик - <see cref="GitWebhookReceiveCommandHandler"/>
/// </summary>
public sealed record GitWebhookReceiveCommand(Guid RepositoryId, IReadOnlyDictionary<string, string> Headers, byte[] Body) : IRequest<GitWebhookResult>;

/// <summary>Id доставок, которые пора разобрать.</summary>
public sealed record GitDueIntegrationJobsQuery(DateTime UtcNow, int Limit = 50) : IRequest<IReadOnlyList<Guid>>;

/// <summary>
/// Разобрать одну доставку; исключение — сбой, его записывает <see cref="GitIntegrationJobFailCommand"/> в новой области.
/// Обработчик - <see cref="GitIntegrationJobHandlers"/>
/// </summary>
public sealed record GitIntegrationJobProcessCommand(Guid DeliveryId) : IRequest<bool>;

/// <summary>
/// Обработчик - <see cref="GitIntegrationJobHandlers"/>
/// </summary>
public sealed record GitIntegrationJobFailCommand(Guid DeliveryId, string Error) : IRequest;

/// <summary>
/// Удалить обработанные доставки старше срока хранения.
/// Обработчик - <see cref="GitIntegrationJobHandlers"/>
/// </summary>
public sealed record GitPurgeIntegrationJobsCommand(DateTime OlderThan) : IRequest<int>;

internal sealed class GitWebhookReceiveCommandHandler(
    IGitHostConnectionRepository connections,
    IGitRepositoryCatalog catalog,
    IGitIntegrationJobRepository jobs,
    IGitSecretProtector protector,
    IUnitOfWork unitOfWork)
    : IRequestHandler<GitWebhookReceiveCommand, GitWebhookResult>
{
    public async Task<GitWebhookResult> Handle(GitWebhookReceiveCommand request, CancellationToken cancellationToken)
    {
        var repository = await catalog.GetByIdAsync(request.RepositoryId, cancellationToken);
        if (repository is not { IsActive: true })
            return GitWebhookResult.NotFound;

        var connection = await connections.GetByIdAsync(repository.ConnectionId, cancellationToken);
        var secret = protector.TryUnprotect(repository.WebhookSecretProtected);
        if (connection is null || secret is null)
            return GitWebhookResult.Unauthorized;

        var headers = new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase);
        string? Header(string name) => headers.TryGetValue(name, out var value) ? value : null;

        if (!GitSignatures.Verify(connection.Provider, Header, request.Body, secret))
            return GitWebhookResult.Unauthorized;

        var eventName = GitPayloadParser.EventName(connection.Provider, Header);
        if (string.IsNullOrWhiteSpace(eventName))
            return GitWebhookResult.BadRequest;

        // Старые GitLab не присылают Id доставки — тогда им служит хеш тела: повтор того же тела не обработается дважды.
        var deliveryId = GitPayloadParser.DeliveryId(connection.Provider, Header) ?? Convert.ToHexStringLower(SHA256.HashData(request.Body));
        if (await jobs.ExistsAsync(repository.Id, deliveryId, cancellationToken))
            return GitWebhookResult.Duplicate;

        GitEvent? parsed;
        try
        {
            parsed = GitPayloadParser.Parse(connection.Provider, eventName, request.Body);
        }
        catch (JsonException)
        {
            return GitWebhookResult.BadRequest;
        }

        jobs.Add(GitIntegrationJob.Create(repository.Id, deliveryId, eventName, parsed?.Serialize()));
        repository.MarkDelivery(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return parsed is null ? GitWebhookResult.Ignored : GitWebhookResult.Accepted;
    }
}

/// <summary>
/// Разбор доставки (docs/TZ_git_integration.md §3). Коды ищутся только в проектах, привязанных к репозиторию; прочие
/// молча пропускаются. Коммиты без кода в ветке с кодом связываются с задачей ветки (кроме ветки по умолчанию).
/// Удалённая ветка закрывает свои связи, но не удаляет их. Связь уникальна — повтор обновляет, не плодит.
/// Автор — по e-mail коммита или по логину из ссылок профиля (GitHub, GitLab, Gitea).
/// Дозагрузка истории (этап 5B) — та же очередь: задание <see cref="GitIntegrationJob.BackfillEvent"/> тянет PR и коммиты
/// ветки по умолчанию через API и прогоняет их тем же разбором, что вебхуки. Исчерпанный лимит запросов — пауза до
/// сброса без траты попытки.
/// </summary>
internal sealed class GitIntegrationJobHandlers(
    IGitHostConnectionRepository connections,
    IGitRepositoryCatalog catalog,
    IGitRepositoryBoardRepository repositoryBoards,
    IGitDevelopmentLinkRepository developmentLinks,
    IGitIntegrationJobRepository jobs,
    ITaskItemRepository tasks,
    IUserRepository users,
    IGitProviderClient client,
    IGitSecretProtector protector,
    GitOptions options,
    GitAutomation automation,
    ISearchIndexQueue searchIndex,
    IUnitOfWork unitOfWork) :
    IRequestHandler<GitDueIntegrationJobsQuery, IReadOnlyList<Guid>>,
    IRequestHandler<GitIntegrationJobProcessCommand, bool>,
    IRequestHandler<GitIntegrationJobFailCommand>,
    IRequestHandler<GitPurgeIntegrationJobsCommand, int>
{
    public Task<IReadOnlyList<Guid>> Handle(GitDueIntegrationJobsQuery request, CancellationToken cancellationToken) =>
        jobs.GetDueIdsAsync(request.UtcNow, request.Limit, cancellationToken);

    public async Task Handle(GitIntegrationJobFailCommand request, CancellationToken cancellationToken)
    {
        if (await jobs.GetByIdAsync(request.DeliveryId, cancellationToken) is not { } delivery)
            return;
        delivery.MarkFailed(request.Error, DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public Task<int> Handle(GitPurgeIntegrationJobsCommand request, CancellationToken cancellationToken) =>
        jobs.PurgeProcessedAsync(request.OlderThan, cancellationToken);

    public async Task<bool> Handle(GitIntegrationJobProcessCommand request, CancellationToken cancellationToken)
    {
        var delivery = await jobs.GetByIdAsync(request.DeliveryId, cancellationToken);
        if (delivery is not { Status: GitIntegrationJobStatus.Pending })
            return false;

        var repository = await catalog.GetByIdAsync(delivery.RepositoryId, cancellationToken);
        var connection = repository is null ? null : await connections.GetByIdAsync(repository.ConnectionId, cancellationToken);
        if (repository is not null && connection is not null)
        {
            var processor = new Processor(repositoryBoards, developmentLinks, tasks, users, automation, searchIndex, repository, connection.Provider, delivery.IsBackfill, cancellationToken);
            if (!delivery.IsBackfill)
            {
                await processor.RunAsync(GitEvent.Deserialize(delivery.Payload));
                await CommentOnNewPullRequestsAsync(connection, repository, processor.NewPullRequests, cancellationToken);
            }
            else if (repository.IsActive)
            {
                var token = protector.TryUnprotect(connection.SecretProtected)
                            ?? throw new InvalidOperationException("Токен подключения не расшифровывается — введите его заново.");
                GitHistory history;
                try
                {
                    history = await client.GetHistoryAsync(connection, token, repository, delivery.ReceivedAt.AddDays(-options.BackfillCommitDays),
                        options.BackfillPullRequests, options.BackfillMaxCommits, cancellationToken);
                }
                catch (GitRateLimitException ex)
                {
                    delivery.Postpone(ex.ResetAt, ex.Message);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    return true;
                }

                // Старые события первыми: PR, обновлённый позже, должен оставить своё состояние последним.
                foreach (var pr in history.PullRequests.OrderBy(p => p.UpdatedAt))
                    await processor.RunAsync(new GitEvent(GitEventKind.PullRequest, PullRequest: pr));
                if (history.Commits.Count > 0)
                    await processor.RunAsync(new GitEvent(GitEventKind.Push, repository.DefaultBranch, history.Commits, history.Commits.Count));
            }
        }

        delivery.MarkDone();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Комментарий «задача Flow» в PR, впервые связанном с задачей (этап 5D), — если привязка это просит и ссылки на
    /// задачу в описании ещё нет (PR, созданный из Flow, её уже несёт). Сбой хостинга — пометка у связи, не ошибка доставки.
    /// </summary>
    private async Task CommentOnNewPullRequestsAsync(GitHostConnection connection, GitRepository repository,
        IReadOnlyList<(TaskItem Task, GitDevelopmentLink Link, GitPullRequest Pr, GitRepositoryBoard Binding)> created, CancellationToken cancellationToken)
    {
        var wanted = created.Where(c => c.Binding.CommentOnPullRequests && c.Pr.State is GitDevelopmentLinkState.Open or GitDevelopmentLinkState.Draft).ToList();
        if (wanted.Count == 0 || protector.TryUnprotect(connection.SecretProtected) is not { } token)
            return;

        foreach (var (task, link, pr, _) in wanted)
        {
            var url = options.TaskUrl(task.Code.Value);
            if (pr.Body?.Contains(url, StringComparison.OrdinalIgnoreCase) == true)
                continue;
            try
            {
                await client.CommentOnPullRequestAsync(connection, token, repository, pr.Number, GitUrls.TaskComment(task.Code.Value, task.Title, url), cancellationToken);
            }
            catch (GitProviderException ex)
            {
                link.SetNote($"Комментарий в PR не оставлен: {ex.Message}");
            }
        }
    }

    /// <param name="history">Дозагрузка истории: связи пишутся, но автопереходы и смарт-коммиты не выполняются —
    /// прошлое не должно двигать задачи сегодня.</param>
    private sealed class Processor(IGitRepositoryBoardRepository repositoryBoards,
        IGitDevelopmentLinkRepository developmentLinks, ITaskItemRepository tasks, IUserRepository users, GitAutomation automation,
        ISearchIndexQueue searchIndex, GitRepository repository, GitProvider provider, bool history, CancellationToken ct)
    {
        private readonly Dictionary<(Guid, GitDevelopmentLinkKind, string), GitDevelopmentLink> _links = [];

        /// <summary>PR, впервые связанные с задачей этим разбором (не дозагрузкой) — для комментария со ссылкой (этап 5D).</summary>
        public List<(TaskItem Task, GitDevelopmentLink Link, GitPullRequest Pr, GitRepositoryBoard Binding)> NewPullRequests { get; } = [];
        private Dictionary<Guid, GitRepositoryBoard>? _boards;
        private IReadOnlyList<User>? _users;

        public async Task RunAsync(GitEvent ev)
        {
            _boards ??= (await repositoryBoards.GetAsync(null, repository.Id, ct)).ToDictionary(b => b.BoardId);
            if (_boards.Count == 0)
                return;

            switch (ev.Kind)
            {
                case GitEventKind.BranchCreated:
                    foreach (var task in await TasksAsync(TaskCodeDetector.FindInBranch(ev.Branch)))
                        await BranchAsync(task, ev.Branch!);
                    break;
                case GitEventKind.BranchDeleted:
                    foreach (var link in await developmentLinks.GetByBranchAsync(repository.Id, ev.Branch!, ct))
                        link.SetState(GitDevelopmentLinkState.Closed);
                    break;
                case GitEventKind.Push:
                    await PushAsync(ev);
                    break;
                case GitEventKind.PullRequest when ev.PullRequest is { } pr:
                    var codes = TaskCodeDetector.Find(pr.Title, pr.Body).Concat(TaskCodeDetector.FindInBranch(pr.SourceBranch)).Distinct().ToList();
                    var author = await UserByLoginAsync(pr.AuthorLogin);
                    foreach (var task in await TasksAsync(codes))
                    {
                        var link = await LinkAsync(task, GitDevelopmentLinkKind.PullRequest, pr.Number);
                        var previous = link.Url.Length == 0 ? (GitDevelopmentLinkState?)null : link.State;
                        if (link.Url.Length == 0 && !history)
                            NewPullRequests.Add((task, link, pr, _boards[task.BoardId]));
                        var indexed = link.Url.Length == 0 ? default : GitSearch.Snapshot(link);
                        link.Apply(pr.Url, pr.Title, pr.State, pr.AuthorLogin, author, pr.SourceBranch, pr.TargetBranch, pr.UpdatedAt);
                        Reindex(link, task, indexed);
                        if (!history)
                            await automation.OnPullRequestAsync(repository, _boards[task.BoardId], task, link, previous, pr, author, ct);
                    }
                    break;
            }
        }

        private async Task PushAsync(GitEvent ev)
        {
            var branch = ev.Branch!;
            var branchTasks = branch == repository.DefaultBranch ? [] : await TasksAsync(TaskCodeDetector.FindInBranch(branch));
            foreach (var task in branchTasks)
                await BranchAsync(task, branch);

            foreach (var commit in ev.Commits ?? [])
            {
                var targets = (await TasksAsync(TaskCodeDetector.Find(commit.Message))).Concat(branchTasks).DistinctBy(t => t.Id).ToList();
                if (targets.Count == 0)
                    continue;

                var author = await UserByEmailAsync(commit.AuthorEmail) ?? await UserByLoginAsync(commit.AuthorLogin);
                var title = commit.Message.Split('\n', 2)[0];
                // Смарт-коммиты — только в ветке по умолчанию: в feature-ветках они срабатывали бы на каждом rebase.
                var smart = new Dictionary<Guid, List<SmartCommand>>();
                if (!history && branch == repository.DefaultBranch)
                    foreach (var command in SmartCommitParser.Parse(commit.Message))
                        foreach (var target in await TasksAsync([command.Code]))
                            (smart.TryGetValue(target.Id, out var list) ? list : smart[target.Id] = []).Add(command);
                foreach (var task in targets)
                {
                    var link = await LinkAsync(task, GitDevelopmentLinkKind.Commit, commit.Sha);
                    // Коммит уже видели в ветке по умолчанию (повторный push, merge-коммит) — команды не выполняем второй раз.
                    var seenOnDefault = link.Url.Length > 0 && link.SourceBranch == repository.DefaultBranch;
                    var indexed = link.Url.Length == 0 ? default : GitSearch.Snapshot(link);
                    link.Apply(commit.Url, title, null, commit.AuthorLogin ?? commit.AuthorEmail, author, branch, null, commit.Timestamp);
                    Reindex(link, task, indexed);
                    if (smart.TryGetValue(task.Id, out var commands) && !seenOnDefault && _boards![task.BoardId].SmartCommits)
                        await automation.OnSmartCommitAsync(task, link, commit, author, commands, ct);
                }
            }
        }

        /// <summary>
        /// Этап 5E: новая связь или изменившийся текст (заголовок PR, ветки) — в очередь поиска; повторный push того же
        /// коммита индекс не трогает. Ветку коммита текст чанка не содержит, но снимок сравнивает и её — лишний upsert
        /// дешёвый: неизменившийся чанк воркер отсекает по ContentHash.
        /// </summary>
        private void Reindex(GitDevelopmentLink link, TaskItem task, (string Title, string? Source, string? Target) before)
        {
            if (before == default || GitSearch.Snapshot(link) != before)
                searchIndex.Upsert(link, task.BoardId, history ? 1 : 0);
        }

        private async Task BranchAsync(TaskItem task, string branch)
        {
            var link = await LinkAsync(task, GitDevelopmentLinkKind.Branch, branch);
            if (link.Url.Length == 0 || link.State == GitDevelopmentLinkState.Closed)
                link.Apply(BranchUrl(branch), branch, GitDevelopmentLinkState.Open, null, null, branch, null, DateTime.UtcNow);
        }

        private string BranchUrl(string branch) => GitUrls.Branch(provider, repository.WebUrl, branch);

        private async Task<GitDevelopmentLink> LinkAsync(TaskItem task, GitDevelopmentLinkKind kind, string externalId)
        {
            var key = (task.Id, kind, externalId);
            if (_links.TryGetValue(key, out var cached))
                return cached;

            var link = await developmentLinks.FindAsync(task.Id, repository.Id, kind, externalId, ct);
            if (link is null)
            {
                link = GitDevelopmentLink.Create(task.Id, repository.Id, kind, externalId);
                developmentLinks.Add(link);
            }

            return _links[key] = link;
        }

        private async Task<List<TaskItem>> TasksAsync(IEnumerable<string> codes)
        {
            var found = new List<TaskItem>();
            foreach (var code in codes)
                if (await tasks.GetByCodeAsync(code, ct) is { } task && _boards!.ContainsKey(task.BoardId) && found.All(t => t.Id != task.Id))
                    found.Add(task);
            return found;
        }

        private async Task<IReadOnlyList<User>> UsersAsync() => _users ??= await users.ListAsync(includeInactive: false, ct);

        private async Task<Guid?> UserByEmailAsync(string? email) =>
            string.IsNullOrWhiteSpace(email) ? null : (await UsersAsync()).FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase))?.Id;

        /// <summary>Логин — последний сегмент ссылки профиля нужного хостинга: github.com/ilya → ilya.</summary>
        private async Task<Guid?> UserByLoginAsync(string? login)
        {
            if (string.IsNullOrWhiteSpace(login))
                return null;
            var type = provider switch
            {
                GitProvider.GitHub => UserLinkType.GitHub,
                GitProvider.GitLab => UserLinkType.GitLab,
                _ => UserLinkType.Gitea
            };
            return (await UsersAsync()).FirstOrDefault(u => u.Links.Any(l => l.Type == type
                && string.Equals(l.Url.TrimEnd('/').Split('/')[^1], login, StringComparison.OrdinalIgnoreCase)))?.Id;
        }
    }
}
