using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class BoardRepository(FlowDbContext db) : IBoardRepository
{
    public Task<Board?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Boards.Include(b => b.Statuses).FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Board>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Boards.Include(b => b.Statuses).AsNoTracking().ToListAsync(cancellationToken);

    public void Add(Board board) => db.Boards.Add(board);

    public void Remove(Board board) => db.Boards.Remove(board);
}
