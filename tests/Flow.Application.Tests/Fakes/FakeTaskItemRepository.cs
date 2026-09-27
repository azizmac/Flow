using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <param name="boards">Доски — чтобы знать финальность статусов (счётчик выполненных подзадач); без них — 0.</param>
public sealed class FakeTaskItemRepository(FakeBoardRepository? boards = null) : ITaskItemRepository
{
    private readonly List<TaskItem> _tasks = [];

    /// <summary>Статусы известны фейку через доски, добавленные в FakeBoardRepository — см. RegisterBoardStatuses.</summary>
    private readonly List<(Guid StatusId, Guid BoardId)> _statusesByBoard = [];

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_tasks.SingleOrDefault(t => t.Id == id));

    public Task<TaskItem?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(_tasks.FirstOrDefault(t => string.Equals(t.Code.Value, code, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks
            .Where(t => t.BoardId == boardId)
            .Where(t => assigneeId is null || t.AssigneeId == assigneeId)
            .ToList());

    public Task<IReadOnlyList<TaskItem>> GetByStatusIdAsync(Guid statusId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.StatusId == statusId).ToList());

    public Task<IReadOnlyList<TaskItem>> SearchAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        var ordered = Filtered(filter)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id);

        // Сортировку по колонкам фейк не воспроизводит — её проверяют интеграционные тесты на настоящем
        // Postgres (порядок задают подзапросы к доскам, статусам и людям). Здесь важно только листание.
        var page = filter.Offset is { } offset ? ordered.Skip(offset) : ordered;

        return Task.FromResult<IReadOnlyList<TaskItem>>(page.Take(filter.Limit).ToList());
    }

    public Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        var byStatus = Filtered(filter with { StatusId = null, StatusType = null })
            .GroupBy(t => t.StatusId)
            .Select(g => (StatusId: g.Key, Count: g.Count()))
            .ToList();

        // Фейк не знает типов статусов (их держит FakeBoardRepository), поэтому разбивка по типу здесь пуста:
        // её проверяют интеграционные тесты на настоящем Postgres.
        var matched = Filtered(filter).Count();

        return Task.FromResult(new TaskCounts(byStatus.Sum(x => x.Count), matched, [], byStatus));
    }

    public Task<IReadOnlyList<TaskItem>> GetBySprintIdAsync(Guid sprintId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.SprintId == sprintId).OrderBy(t => t.Rank, StringComparer.Ordinal).ToList());

    public Task<IReadOnlyList<TaskItem>> GetByMilestoneIdAsync(Guid milestoneId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.MilestoneId == milestoneId).ToList());

    /// <summary>Та же арифметика, что GROUP BY в TaskItemRepository; финальность и вид статуса — из досок фейка.</summary>
    public async Task<IReadOnlyDictionary<Guid, MilestoneCounts>> CountByMilestonesAsync(
        IReadOnlyCollection<Guid> milestoneIds, DateOnly today, DateTime closedSince, CancellationToken cancellationToken)
    {
        var statuses = boards is null
            ? new Dictionary<Guid, Status>()
            : (await boards.GetAllAsync(cancellationToken)).SelectMany(b => b.Statuses).ToDictionary(s => s.Id);
        bool Final(TaskItem t) => statuses.TryGetValue(t.StatusId, out var s) && s.IsFinal;
        bool Working(TaskItem t) => statuses.TryGetValue(t.StatusId, out var s) && s.Type is StatusType.InProgress or StatusType.InReview;

        return _tasks
            .Where(t => t.MilestoneId is { } m && milestoneIds.Contains(m))
            .GroupBy(t => t.MilestoneId!.Value)
            .ToDictionary(g => g.Key, g => new MilestoneCounts(
                g.Count(),
                g.Count(Final),
                g.Count(t => !Final(t) && Working(t)),
                g.Sum(t => t.StoryPoints ?? 0),
                g.Where(Final).Sum(t => t.StoryPoints ?? 0),
                g.Count(t => !Final(t) && t.DueDate < today),
                g.Count(t => Final(t) && t.StatusChangedAt >= closedSince)));
    }

    public Task<IReadOnlyList<TaskGroupCount>> GroupCountAsync(
        TaskListFilter filter, TaskGroupField field, IReadOnlyList<Guid>? customFieldIds, CancellationToken cancellationToken)
    {
        var items = Filtered(filter).ToList();
        IEnumerable<string?> Keys(TaskItem t) => field switch
        {
            TaskGroupField.Status => [t.StatusId.ToString()],
            TaskGroupField.Assignee => [t.AssigneeId?.ToString()],
            TaskGroupField.Priority => [((int)t.Priority).ToString()],
            TaskGroupField.Type => [t.TypeId.ToString()],
            TaskGroupField.Board => [t.BoardId.ToString()],
            _ => (customFieldIds ?? []).Select(t.GetCustomField).FirstOrDefault(v => v is not null) switch
            {
                { ValueKind: System.Text.Json.JsonValueKind.Array } arr when arr.GetArrayLength() > 0 => arr.EnumerateArray().Select(e => (string?)e.ToString()),
                { ValueKind: not System.Text.Json.JsonValueKind.Array } one => [one.ToString()],
                _ => [null]
            }
        };

        return Task.FromResult<IReadOnlyList<TaskGroupCount>>(items.SelectMany(Keys)
            .GroupBy(k => k)
            .Select(g => new TaskGroupCount(g.Key, g.Count()))
            .ToList());
    }

    public async Task<TaskDailyCounts> DailyCountsAsync(TaskListFilter filter, DateTime since, CancellationToken cancellationToken)
    {
        var finalStatuses = boards is null
            ? []
            : (await boards.GetAllAsync(cancellationToken)).SelectMany(b => b.Statuses).Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();
        var items = Filtered(filter).ToList();
        return new TaskDailyCounts(
            items.Where(t => t.CreatedAt >= since).GroupBy(t => DateOnly.FromDateTime(t.CreatedAt)).ToDictionary(g => g.Key, g => g.Count()),
            items.Where(t => finalStatuses.Contains(t.StatusId) && t.StatusChangedAt >= since)
                .GroupBy(t => DateOnly.FromDateTime(t.StatusChangedAt)).ToDictionary(g => g.Key, g => g.Count()));
    }

    public Task<IReadOnlyList<Guid>> MatchingIdsAsync(TaskListFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Filtered(filter).Select(t => t.Id).ToList());

    private IEnumerable<TaskItem> Filtered(TaskListFilter filter) => _tasks
        .Where(t => filter.BoardId is null || t.BoardId == filter.BoardId)
        .Where(t => filter.VisibleBoardIds is null || filter.VisibleBoardIds.Contains(t.BoardId))
        .Where(t => filter.AssigneeId is null || t.AssigneeId == filter.AssigneeId)
        .Where(t => !filter.Unassigned || t.AssigneeId is null)
        .Where(t => filter.StatusId is null || t.StatusId == filter.StatusId)
        .Where(t => filter.ParentId is null || t.ParentId == filter.ParentId)
        .Where(t => filter.Query is null
                    || t.Title.Contains(filter.Query, StringComparison.OrdinalIgnoreCase)
                    || t.Code.Value.Contains(filter.Query, StringComparison.OrdinalIgnoreCase))
        .Where(t => filter.BeforeCreatedAt is not { } at || filter.BeforeId is not { } id
                    || t.CreatedAt < at || (t.CreatedAt == at && t.Id.CompareTo(id) < 0));

    public Task<IReadOnlyDictionary<Guid, int>> CountByBoardIdsAsync(
        IReadOnlyCollection<Guid> boardIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(_tasks
            .Where(t => boardIds.Contains(t.BoardId))
            .GroupBy(t => t.BoardId)
            .ToDictionary(g => g.Key, g => g.Count()));

    public async Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken) =>
        _statusesByBoard.Contains((statusId, boardId))
        || (boards is not null && (await boards.GetByIdAsync(boardId, cancellationToken))?.Statuses.Any(s => s.Id == statusId) == true);

    public Task<IReadOnlyList<TaskItem>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.ParentId == parentId).OrderBy(t => t.Rank, StringComparer.Ordinal).ToList());

    public async Task<IReadOnlyDictionary<Guid, ChildCounts>> CountChildrenAsync(IReadOnlyCollection<Guid> parentIds, CancellationToken cancellationToken)
    {
        var finalStatuses = boards is null
            ? new HashSet<Guid>()
            : (await boards.GetAllAsync(cancellationToken)).SelectMany(b => b.Statuses).Where(s => s.IsFinal).Select(s => s.Id).ToHashSet();

        return _tasks
            .Where(t => t.ParentId is { } p && parentIds.Contains(p))
            .GroupBy(t => t.ParentId!.Value)
            .ToDictionary(g => g.Key, g => new ChildCounts(g.Count(), g.Count(t => finalStatuses.Contains(t.StatusId))));
    }

    /// <summary>Обход в глубину по рангу — тот же порядок, что даёт рекурсивный CTE в TaskItemRepository.</summary>
    public Task<IReadOnlyList<TaskTreeEntry>> GetTreeAsync(Guid boardId, Guid? rootId, CancellationToken cancellationToken)
    {
        var result = new List<TaskTreeEntry>();

        void Walk(TaskItem task, int depth)
        {
            result.Add(new TaskTreeEntry(task, depth));
            foreach (var child in _tasks.Where(t => t.ParentId == task.Id).OrderBy(t => t.Rank, StringComparer.Ordinal))
                Walk(child, depth + 1);
        }

        var roots = rootId is { } id
            ? _tasks.Where(t => t.Id == id && t.BoardId == boardId)
            : _tasks.Where(t => t.BoardId == boardId && t.ParentId is null);
        foreach (var root in roots.OrderBy(t => t.Rank, StringComparer.Ordinal).ToList())
            Walk(root, 0);

        return Task.FromResult<IReadOnlyList<TaskTreeEntry>>(result);
    }

    public Task<string?> GetMaxRankAsync(Guid boardId, Guid? excludeTaskId, CancellationToken cancellationToken) =>
        Task.FromResult(_tasks.Where(t => t.BoardId == boardId && t.Id != excludeTaskId).Select(t => t.Rank).Max(StringComparer.Ordinal));

    public Task<string?> GetNeighborRankAsync(Guid boardId, string rank, bool after, Guid excludeTaskId, CancellationToken cancellationToken)
    {
        var ranks = _tasks.Where(t => t.BoardId == boardId && t.Id != excludeTaskId).Select(t => t.Rank);
        return Task.FromResult(after
            ? ranks.Where(r => string.CompareOrdinal(r, rank) > 0).Min(StringComparer.Ordinal)
            : ranks.Where(r => string.CompareOrdinal(r, rank) < 0).Max(StringComparer.Ordinal));
    }

    public void Add(TaskItem task) => _tasks.Add(task);

    public void Remove(TaskItem task) => _tasks.Remove(task);

    /// <summary>Тесты вызывают это после Board.Create(...)/AddStatus(...), чтобы StatusBelongsToBoardAsync знал про статусы доски.</summary>
    public void RegisterBoardStatuses(Board board)
    {
        foreach (var status in board.Statuses)
            _statusesByBoard.Add((status.Id, board.Id));
    }
}
