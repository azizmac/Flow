using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskItemRepository : ITaskItemRepository
{
    private readonly List<TaskItem> _tasks = [];

    /// <summary>Статусы известны фейку через доски, добавленные в FakeBoardRepository — см. RegisterBoardStatuses.</summary>
    private readonly List<(Guid StatusId, Guid BoardId)> _statusesByBoard = [];

    public Task<TaskItem?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_tasks.SingleOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, Guid? assigneeId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks
            .Where(t => t.BoardId == boardId)
            .Where(t => assigneeId is null || t.AssigneeId == assigneeId)
            .ToList());

    public Task<IReadOnlyList<TaskItem>> SearchAsync(TaskListFilter filter, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(Filtered(filter)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Take(filter.Limit)
            .ToList());

    public Task<TaskCounts> CountAsync(TaskListFilter filter, CancellationToken cancellationToken)
    {
        var byStatus = Filtered(filter with { StatusId = null, StatusType = null })
            .GroupBy(t => t.StatusId)
            .Select(g => (StatusId: g.Key, Count: g.Count()))
            .ToList();

        // Фейк не знает типов статусов (их держит FakeBoardRepository), поэтому разбивка по типу здесь пуста:
        // её проверяют интеграционные тесты на настоящем Postgres.
        return Task.FromResult(new TaskCounts(byStatus.Sum(x => x.Count), [], byStatus));
    }

    private IEnumerable<TaskItem> Filtered(TaskListFilter filter) => _tasks
        .Where(t => filter.BoardId is null || t.BoardId == filter.BoardId)
        .Where(t => filter.AssigneeId is null || t.AssigneeId == filter.AssigneeId)
        .Where(t => !filter.Unassigned || t.AssigneeId is null)
        .Where(t => filter.StatusId is null || t.StatusId == filter.StatusId)
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

    public Task<bool> StatusBelongsToBoardAsync(Guid statusId, Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult(_statusesByBoard.Contains((statusId, boardId)));

    public void Add(TaskItem task) => _tasks.Add(task);

    public void Remove(TaskItem task) => _tasks.Remove(task);

    /// <summary>Тесты вызывают это после Board.Create(...)/AddStatus(...), чтобы StatusBelongsToBoardAsync знал про статусы доски.</summary>
    public void RegisterBoardStatuses(Board board)
    {
        foreach (var status in board.Statuses)
            _statusesByBoard.Add((status.Id, board.Id));
    }
}
