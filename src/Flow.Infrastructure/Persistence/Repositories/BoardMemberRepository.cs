using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class BoardMemberRepository(FlowDbContext db) : IBoardMemberRepository
{
    /// <summary>Потолок проекта и участие — одним запросом: проверка прав стоит в каждой изменяющей команде.</summary>
    public Task<BoardAccessData?> GetAccessDataAsync(Guid boardId, Guid userId, CancellationToken cancellationToken) =>
        db.Boards
            .Where(b => b.Id == boardId)
            .Select(b => new BoardAccessData(
                b.Id,
                b.Visibility,
                b.DefaultRole,
                db.BoardMembers.Where(m => m.BoardId == b.Id && m.UserId == userId).Select(m => (ProjectRole?)m.Role).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<BoardAccessData>> GetAccessDataForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Boards
            .Select(b => new BoardAccessData(
                b.Id,
                b.Visibility,
                b.DefaultRole,
                db.BoardMembers.Where(m => m.BoardId == b.Id && m.UserId == userId).Select(m => (ProjectRole?)m.Role).FirstOrDefault()))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>?> GetVisibleBoardIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Приватных проектов нет — фильтр не нужен: запросы чтения идут как до этапа 4B, без ANY(@visible).
        if (!await db.Boards.AnyAsync(b => b.Visibility == BoardVisibility.Private, cancellationToken))
            return null;

        return await db.Boards
            .Where(b => b.Visibility == BoardVisibility.Open || db.BoardMembers.Any(m => m.BoardId == b.Id && m.UserId == userId))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<BoardMember?> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken) =>
        db.BoardMembers.FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<BoardMember>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        await db.BoardMembers.Where(m => m.BoardId == boardId).AsNoTracking().ToListAsync(cancellationToken);

    public void Add(BoardMember member) => db.BoardMembers.Add(member);

    public void Remove(BoardMember member) => db.BoardMembers.Remove(member);
}
