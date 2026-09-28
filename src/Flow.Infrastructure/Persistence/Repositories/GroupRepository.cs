using Flow.Application.Abstractions;
using Flow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flow.Infrastructure.Persistence.Repositories;

public sealed class GroupRepository(FlowDbContext db) : IGroupRepository
{
    public Task<Group?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Groups.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Groups.OrderBy(g => g.Name).ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetMemberIdsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken) =>
        (await db.GroupMembers.Where(m => groupIds.Contains(m.GroupId)).OrderBy(m => m.AddedAt).Select(m => new { m.GroupId, m.UserId }).ToListAsync(cancellationToken))
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(m => m.UserId).ToList());

    public Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken) =>
        db.GroupMembers.FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, cancellationToken);

    public void Add(Group group) => db.Groups.Add(group);

    public void Remove(Group group) => db.Groups.Remove(group);

    public void AddMember(GroupMember member) => db.GroupMembers.Add(member);

    public void RemoveMember(GroupMember member) => db.GroupMembers.Remove(member);
}
