using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class BoardRepository(FlowDbContext db) : IBoardRepository
{
    public Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Boards.Include(b => b.Statuses).Include(b => b.TaskTypes).Include(b => b.Transitions).Include(b => b.CustomFields).Include(b => b.Screens).Include(b => b.CodeRepositories).AsSplitQuery().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Board>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Boards.Include(b => b.Statuses).Include(b => b.TaskTypes).Include(b => b.Transitions).Include(b => b.CustomFields).Include(b => b.Screens).Include(b => b.CodeRepositories).AsSplitQuery().AsNoTracking().ToListAsync(cancellationToken);

    public Task<bool> ExistsByKeyAsync(string key, CancellationToken cancellationToken) =>
        db.Boards.AnyAsync(b => b.Key == key, cancellationToken);

    public void Add(Board board) => db.Boards.Add(board);

    /// <summary>
    /// Порядок важен. FK TaskItems.StatusId объявлен как Restrict, поэтому:
    /// 1) если задачи не в трекере, EF удалит статусы явно, а БД отклонит это, пока на них ссылаются задачи (23503);
    /// 2) если задачи в трекере, но ещё не помечены Deleted, EF при каскаде Board → Status
    ///    увидит "разорванную" обязательную связь Status → TaskItem и бросит InvalidOperationException.
    /// Поэтому сначала догружаем задачи и помечаем их Deleted, и только потом удаляем доску (каскад на статусы).
    /// То же с типами задач (FK TaskItems.TypeId — тоже Restrict): GetByIdAsync грузит их вместе с доской.
    /// </summary>
    public async Task RemoveAsync(Board board, CancellationToken cancellationToken)
    {
        await db.Entry(board).Collection(b => b.Tasks).LoadAsync(cancellationToken);
        db.TaskItems.RemoveRange(board.Tasks);
        db.Boards.Remove(board);
    }
}
