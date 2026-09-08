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

    public Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.BoardId == boardId).ToList());

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
