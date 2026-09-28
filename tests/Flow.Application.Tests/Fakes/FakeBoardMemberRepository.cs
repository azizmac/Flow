using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <summary>Участники проектов в памяти; потолок роли берётся у досок из FakeBoardRepository, как join в БД.</summary>
public sealed class FakeBoardMemberRepository(FakeBoardRepository boards, FakeGroupRepository groups) : IBoardMemberRepository
{
    private readonly List<BoardMember> _members = [];
    private readonly List<BoardGroup> _groups = [];

    /// <summary>Участия человека: прямое и через группы (удалённые группы не считаются), с ролью и набором каждое.</summary>
    private List<AccessGrant> GrantsOf(Guid boardId, Guid userId) =>
        _members.Where(m => m.BoardId == boardId && m.UserId == userId).Select(m => new AccessGrant(m.Role, m.PermissionSetId))
            .Concat(LiveGroups(boardId).Where(g => groups.Members.Any(m => m.GroupId == g.GroupId && m.UserId == userId))
                .Select(g => new AccessGrant(g.Role, g.PermissionSetId)))
            .ToList();

    private IEnumerable<BoardGroup> LiveGroups(Guid boardId) => _groups.Where(g => g.BoardId == boardId && groups.Groups.Any(x => x.Id == g.GroupId));

    public IReadOnlyList<BoardMember> All => _members;

    public async Task<BoardAccessData?> GetAccessDataAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(boardId, cancellationToken);
        return board is null
            ? null
            : new BoardAccessData(board.Id, board.Visibility, board.DefaultRole, GrantsOf(board.Id, userId));
    }

    public async Task<IReadOnlyList<BoardAccessData>> GetAccessDataForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await boards.GetAllAsync(cancellationToken))
            .Select(b => new BoardAccessData(b.Id, b.Visibility, b.DefaultRole, GrantsOf(b.Id, userId)))
            .ToList();

    public async Task<IReadOnlyList<Guid>?> GetVisibleBoardIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await boards.GetAllAsync(cancellationToken);
        if (all.All(b => b.Visibility == BoardVisibility.Open))
            return null;

        return all
            .Where(b => b.Visibility == BoardVisibility.Open || GrantsOf(b.Id, userId).Count > 0)
            .Select(b => b.Id)
            .ToList();
    }

    public Task<BoardMember?> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_members.SingleOrDefault(m => m.BoardId == boardId && m.UserId == userId));

    public Task<IReadOnlyList<BoardMember>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoardMember>>(_members.Where(m => m.BoardId == boardId).ToList());

    public Task<IReadOnlyList<BoardGroupView>> GetGroupsByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoardGroupView>>(LiveGroups(boardId)
            .Select(g => new BoardGroupView(g, groups.Groups.Single(x => x.Id == g.GroupId).Name, groups.Members.Count(m => m.GroupId == g.GroupId)))
            .ToList());

    public Task<BoardGroup?> GetGroupAsync(Guid boardId, Guid groupId, CancellationToken cancellationToken) =>
        Task.FromResult(LiveGroups(boardId).FirstOrDefault(g => g.GroupId == groupId));

    public void AddGroup(BoardGroup group) => _groups.Add(group);

    public void RemoveGroup(BoardGroup group) => _groups.Remove(group);

    public void Add(BoardMember member) => _members.Add(member);

    public void Remove(BoardMember member) => _members.Remove(member);
}
