using Flow.Application.Abstractions;
using Flow.Application.Exceptions;
using Flow.Application.Features.Tasks;
using Flow.Application.Features.Tasks.Mentions;
using Flow.Application.Security;
using Flow.Domain.Entities;
using Flow.Shared.Contracts.Search;

namespace Flow.Application.Features.Scm;

/// <summary>
/// Автоматизация по событиям хостинга (docs/TZ_scm_integration.md §4–5, этап 5C). Всё, что меняет задачу, идёт тем же
/// путём, что правка человеком: проверка workflow через <see cref="TransitionGuard"/> (обхода нет), журнал, очередь
/// поиска. Отказ — не ошибка доставки, а пометка на связи (<see cref="ScmLink.Note"/>): её видно в блоке «Разработка».
/// <list type="bullet">
/// <item>Автопереход: PR открыт (новый или вышел из черновика) или влит в ветку по умолчанию → статус из настроек
/// привязки. Задача уже в финальном статусе — не трогаем; переоткрытый PR назад не переводит. Actor — автор PR, если он
/// сопоставлен, активен и может править задачу, иначе <see cref="ScmBot"/>.</item>
/// <item>Смарт-коммиты — только в push'ах в ветку по умолчанию, только от сопоставленного активного автора с правами
/// на действие; бот их не выполняет, иначе любой с правом push закрывал бы задачи.</item>
/// </list>
/// </summary>
internal sealed class ScmAutomation(
    IBoardRepository boards,
    IUserRepository users,
    IProjectAccess projectAccess,
    IPermissionService permissions,
    TransitionGuard guard,
    ITaskActivityRepository activities,
    ITaskCommentRepository comments,
    MentionResolver mentions,
    ISearchIndexQueue searchIndex)
{
    private readonly Dictionary<Guid, Board?> _boards = [];
    private User? _bot;

    public async Task OnPullRequestAsync(ScmRepository repository, ScmRepositoryBoard binding, TaskItem task, ScmLink link,
        ScmLinkState? previous, ScmPullRequest pr, Guid? authorId, CancellationToken ct)
    {
        Guid? target = null;
        if (pr.State == ScmLinkState.Open && previous is null or ScmLinkState.Draft)
            target = binding.OnPullRequestOpenedStatusId;
        else if (pr.State == ScmLinkState.Merged && previous != ScmLinkState.Merged && pr.TargetBranch == repository.DefaultBranch)
            target = binding.OnPullRequestMergedStatusId;
        if (target is not { } statusId || await BoardAsync(task.BoardId, ct) is not { } board)
            return;

        var current = board.Statuses.FirstOrDefault(s => s.Id == task.StatusId);
        var status = board.Statuses.FirstOrDefault(s => s.Id == statusId);
        if (status is null || current is null || current.IsFinal || task.StatusId == statusId)
            return;

        var (actor, access) = await AuthorOrBotAsync(authorId, task, ct);
        var check = await guard.CheckAsync(access, board, task, statusId, ct);
        if (!check.Allowed)
        {
            link.SetNote($"Автопереход в «{status.Name}» не разрешён workflow: {string.Join("; ", check.Reasons)}");
            return;
        }

        ChangeStatus(task, actor.Id, statusId, $"PR #{pr.Number}", pr.Url);
        link.SetNote(null);
    }

    /// <summary>Команды одного коммита для одной задачи; commands уже отобраны по коду этой задачи.</summary>
    public async Task OnSmartCommitAsync(TaskItem task, ScmLink link, ScmCommit commit, Guid? authorId,
        IReadOnlyList<SmartCommand> commands, CancellationToken ct)
    {
        if (commands.Count == 0)
            return;

        var source = $"коммит {(commit.Sha.Length > 7 ? commit.Sha[..7] : commit.Sha)}";
        var author = authorId is { } id ? await users.GetByIdAsync(id, ct) : null;
        if (author is not { IsActive: true })
        {
            link.SetNote("Смарт-коммит не выполнен: автор коммита не сопоставлен с активным пользователем Flow.");
            return;
        }

        if (await BoardAsync(task.BoardId, ct) is not { } board)
            return;

        var problems = new List<string>();
        try
        {
            var access = await projectAccess.GetAsync(author, task.BoardId, ct);
            foreach (var command in commands)
            {
                switch (command.Kind)
                {
                    case SmartCommandKind.Comment:
                        permissions.EnsureCanComment(access);
                        var body = command.Argument!;
                        var comment = TaskComment.Create(task.Id, author.Id, body, await mentions.ResolveAsync(body, ct));
                        comments.Add(comment);
                        activities.Add(TaskActivity.CommentAdded(task.Id, author.Id, comment.Id).FromSource(source, commit.Url));
                        searchIndex.Enqueue(SearchSourceType.Comment, comment.Id, task.BoardId, SearchIndexOperation.Upsert);
                        break;
                    default:
                        permissions.EnsureCanEditTask(author, access, task);
                        var status = command.Kind == SmartCommandKind.Done
                            ? board.Statuses.Where(s => s.IsFinal).OrderBy(s => s.SortOrder).FirstOrDefault()
                            : board.Statuses.FirstOrDefault(s => string.Equals(s.Name, command.Argument, StringComparison.OrdinalIgnoreCase));
                        if (status is null)
                        {
                            problems.Add($"статуса «{command.Argument}» нет в проекте");
                            break;
                        }
                        if (status.Id == task.StatusId)
                            break;

                        var check = await guard.CheckAsync(access, board, task, status.Id, ct);
                        if (!check.Allowed)
                        {
                            problems.Add($"переход в «{status.Name}» не разрешён workflow: {string.Join("; ", check.Reasons)}");
                            break;
                        }

                        ChangeStatus(task, author.Id, status.Id, source, commit.Url);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is ForbiddenException or ProjectNotFoundException)
        {
            problems.Add("у автора коммита нет прав на это действие в проекте");
        }

        link.SetNote(problems.Count == 0 ? null : $"Смарт-коммит не выполнен: {string.Join("; ", problems)}.");
    }

    private void ChangeStatus(TaskItem task, Guid actorId, Guid statusId, string source, string? url)
    {
        activities.Add(TaskActivity.StatusChanged(task.Id, actorId, task.StatusId, statusId).FromSource(source, url));
        task.ChangeStatus(statusId);
        // Текст не менялся — воркер обновит только IsClosed, без реэмбеддинга.
        searchIndex.Enqueue(SearchSourceType.Task, task.Id, task.BoardId, SearchIndexOperation.Upsert);
    }

    private async Task<(User Actor, ProjectAccessInfo Access)> AuthorOrBotAsync(Guid? authorId, TaskItem task, CancellationToken ct)
    {
        if (authorId is { } id && await users.GetByIdAsync(id, ct) is { IsActive: true } author)
        {
            try
            {
                var access = await projectAccess.GetAsync(author, task.BoardId, ct);
                permissions.EnsureCanEditTask(author, access, task);
                return (author, access);
            }
            catch (Exception ex) when (ex is ForbiddenException or ProjectNotFoundException)
            {
                // Автор не может править задачу — переход делает бот, с правами участника проекта.
            }
        }

        if (_bot is null)
        {
            _bot = await users.GetByIdAsync(ScmBot.Id, ct);
            if (_bot is null)
            {
                _bot = ScmBot.Create();
                users.Add(_bot);
            }
        }

        return (_bot, new ProjectAccessInfo(task.BoardId, ProjectRole.Member, ProjectRoles.PermissionsOf(ProjectRole.Member)));
    }

    private async Task<Board?> BoardAsync(Guid boardId, CancellationToken ct)
    {
        if (!_boards.TryGetValue(boardId, out var board))
            _boards[boardId] = board = await boards.GetByIdAsync(boardId, ct);
        return board;
    }
}
