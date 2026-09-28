using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class BoardMemberRepository(FlowDbContext db) : IBoardMemberRepository
{
    /// <summary>
    /// Потолок проекта и участие — одним запросом: проверка прав стоит в каждой изменяющей команде. Роль участия —
    /// максимум из прямой и ролей групп человека в проекте (этап 4C); две подзапросные колонки складываются здесь же.
    /// </summary>
    public async Task<BoardAccessData?> GetAccessDataAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var row = await AccessRows(db.Boards.Where(b => b.Id == boardId), userId).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToData(row);
    }

    public async Task<IReadOnlyList<BoardAccessData>> GetAccessDataForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await AccessRows(db.Boards, userId).ToListAsync(cancellationToken)).Select(ToData).ToList();

    public async Task<IReadOnlyList<Guid>?> GetVisibleBoardIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        // Приватных проектов нет — фильтр не нужен: запросы чтения идут как до этапа 4B, без ANY(@visible).
        if (!await db.Boards.AnyAsync(b => b.Visibility == BoardVisibility.Private, cancellationToken))
            return null;

        return await db.Boards
            .Where(b => b.Visibility == BoardVisibility.Open
                        || db.BoardMembers.Any(m => m.BoardId == b.Id && m.UserId == userId)
                        || db.BoardGroups.Any(g => g.BoardId == b.Id && db.GroupMembers.Any(gm => gm.GroupId == g.GroupId && gm.UserId == userId)))
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
    }

    public Task<BoardMember?> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken) =>
        db.BoardMembers.FirstOrDefaultAsync(m => m.BoardId == boardId && m.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<BoardMember>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        await db.BoardMembers.Where(m => m.BoardId == boardId).AsNoTracking().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BoardGroupView>> GetGroupsByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        (await db.BoardGroups.AsNoTracking()
            .Where(l => l.BoardId == boardId)
            .Join(db.Groups, l => l.GroupId, g => g.Id, (l, g) => new { Link = l, g.Name, Count = db.GroupMembers.Count(m => m.GroupId == g.Id) })
            .ToListAsync(cancellationToken))
        .Select(x => new BoardGroupView(x.Link, x.Name, x.Count))
        .ToList();

    public Task<BoardGroup?> GetGroupAsync(Guid boardId, Guid groupId, CancellationToken cancellationToken) =>
        db.BoardGroups.FirstOrDefaultAsync(g => g.BoardId == boardId && g.GroupId == groupId, cancellationToken);

    public void AddGroup(BoardGroup group) => db.BoardGroups.Add(group);

    public void RemoveGroup(BoardGroup group) => db.BoardGroups.Remove(group);

    public void Add(BoardMember member) => db.BoardMembers.Add(member);

    public void Remove(BoardMember member) => db.BoardMembers.Remove(member);

    private sealed class AccessRow
    {
        public Guid BoardId { get; init; }
        public BoardVisibility Visibility { get; init; }
        public ProjectRole? DefaultRole { get; init; }
        public List<AccessGrant> Direct { get; init; } = [];
        public List<AccessGrant> ViaGroups { get; init; } = [];
    }

    /// <summary>Участия человека — прямое и через группы, с ролью и набором прав каждое (этапы 4C, 4E).</summary>
    private IQueryable<AccessRow> AccessRows(IQueryable<Board> boards, Guid userId) =>
        boards.Select(b => new AccessRow
        {
            BoardId = b.Id,
            Visibility = b.Visibility,
            DefaultRole = b.DefaultRole,
            Direct = db.BoardMembers.Where(m => m.BoardId == b.Id && m.UserId == userId)
                .Select(m => new AccessGrant(m.Role, m.PermissionSetId)).ToList(),
            ViaGroups = db.BoardGroups
                .Where(g => g.BoardId == b.Id && db.GroupMembers.Any(gm => gm.GroupId == g.GroupId && gm.UserId == userId))
                .Select(g => new AccessGrant(g.Role, g.PermissionSetId)).ToList()
        });

    private static BoardAccessData ToData(AccessRow row) =>
        new(row.BoardId, row.Visibility, row.DefaultRole, row.Direct.Concat(row.ViaGroups).ToList());
}
