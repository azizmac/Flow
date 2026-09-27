using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

/// <summary>Участники проектов в памяти; потолок роли берётся у досок из FakeBoardRepository, как join в БД.</summary>
public sealed class FakeBoardMemberRepository(FakeBoardRepository boards) : IBoardMemberRepository
{
    private readonly List<BoardMember> _members = [];

    public IReadOnlyList<BoardMember> All => _members;

    public async Task<BoardAccessData?> GetAccessDataAsync(Guid boardId, Guid userId, CancellationToken cancellationToken)
    {
        var board = await boards.GetByIdAsync(boardId, cancellationToken);
        return board is null
            ? null
            : new BoardAccessData(board.Id, board.Visibility, board.DefaultRole, _members.SingleOrDefault(m => m.BoardId == boardId && m.UserId == userId)?.Role);
    }

    public async Task<IReadOnlyList<BoardAccessData>> GetAccessDataForUserAsync(Guid userId, CancellationToken cancellationToken) =>
        (await boards.GetAllAsync(cancellationToken))
            .Select(b => new BoardAccessData(b.Id, b.Visibility, b.DefaultRole, _members.SingleOrDefault(m => m.BoardId == b.Id && m.UserId == userId)?.Role))
            .ToList();

    public async Task<IReadOnlyList<Guid>?> GetVisibleBoardIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await boards.GetAllAsync(cancellationToken);
        if (all.All(b => b.Visibility == BoardVisibility.Open))
            return null;

        return all
            .Where(b => b.Visibility == BoardVisibility.Open || _members.Any(m => m.BoardId == b.Id && m.UserId == userId))
            .Select(b => b.Id)
            .ToList();
    }

    public Task<BoardMember?> GetAsync(Guid boardId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_members.SingleOrDefault(m => m.BoardId == boardId && m.UserId == userId));

    public Task<IReadOnlyList<BoardMember>> GetByBoardAsync(Guid boardId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BoardMember>>(_members.Where(m => m.BoardId == boardId).ToList());

    public void Add(BoardMember member) => _members.Add(member);

    public void Remove(BoardMember member) => _members.Remove(member);
}
