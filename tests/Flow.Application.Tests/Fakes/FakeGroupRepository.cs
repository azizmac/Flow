using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGroupRepository : IGroupRepository
{
    public List<Group> Groups { get; } = [];

    public List<GroupMember> Members { get; } = [];

    public Task<Group?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Groups.FirstOrDefault(g => g.Id == id));

    public Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Group>>(Groups.OrderBy(g => g.Name).ToList());

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetMemberIdsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(Members.Where(m => groupIds.Contains(m.GroupId)).GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(m => m.UserId).ToList()));

    public Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(Members.FirstOrDefault(m => m.GroupId == groupId && m.UserId == userId));

    public void Add(Group group) => Groups.Add(group);

    /// <summary>Каскад, как в БД: состав уходит вместе с группой (роли в проектах чистит FakeBoardMemberRepository по отсутствию группы).</summary>
    public void Remove(Group group)
    {
        Groups.Remove(group);
        Members.RemoveAll(m => m.GroupId == group.Id);
    }

    public void AddMember(GroupMember member) => Members.Add(member);

    public void RemoveMember(GroupMember member) => Members.Remove(member);
}
