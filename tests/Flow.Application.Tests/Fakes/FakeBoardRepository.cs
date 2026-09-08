using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Flow.Shared.Ids;

namespace Flow.Application.Tests.Fakes;

/// <summary>
/// Простая in-memory реализация вместо мока — для этих интерфейсов (Get/Add/Remove)
/// поведение репозитория тривиально, отдельная mocking-библиотека тут не нужна.
/// </summary>
public sealed class FakeBoardRepository : IBoardRepository
{
    private readonly List<Board> _boards = [];

    public Task<Board?> GetByIdAsync(BoardId id, CancellationToken cancellationToken) =>
        Task.FromResult(_boards.SingleOrDefault(b => b.Id == id));

    public Task<IReadOnlyList<Board>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Board>>(_boards.ToList());

    public Task<bool> ExistsByKeyAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_boards.Any(b => b.Key == key));

    public void Add(Board board) => _boards.Add(board);

    public Task RemoveAsync(Board board, CancellationToken cancellationToken)
    {
        _boards.Remove(board);
        return Task.CompletedTask;
    }
}
