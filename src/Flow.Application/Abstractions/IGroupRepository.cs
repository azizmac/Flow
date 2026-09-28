using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Группы людей и их состав (этап 4C).</summary>
public interface IGroupRepository
{
    Task<Group?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Все группы по имени.</summary>
    Task<IReadOnlyList<Group>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Состав групп: Id группы → участники.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetMemberIdsAsync(IReadOnlyCollection<Guid> groupIds, CancellationToken cancellationToken);

    Task<GroupMember?> GetMemberAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);

    void Add(Group group);

    void Remove(Group group);

    void AddMember(GroupMember member);

    void RemoveMember(GroupMember member);
}
