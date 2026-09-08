using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Ids;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeTaskItemRepository : ITaskItemRepository
{
    private readonly List<TaskItem> _tasks = [];

    /// <summary>Статусы известны фейку через доски, добавленные в FakeBoardRepository — см. RegisterBoardStatuses.</summary>
    private readonly List<(StatusId StatusId, BoardId BoardId)> _statusesByBoard = [];

    public Task<TaskItem?> GetByIdAsync(TaskId id, CancellationToken cancellationToken) =>
        Task.FromResult(_tasks.SingleOrDefault(t => t.Id == id));

    public Task<IReadOnlyList<TaskItem>> GetByBoardIdAsync(BoardId boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TaskItem>>(_tasks.Where(t => t.BoardId == boardId).ToList());

    public Task<bool> StatusBelongsToBoardAsync(StatusId statusId, BoardId boardId, CancellationToken cancellationToken) =>
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
