using Flow.Domain.Entities;

namespace Flow.Application.Abstractions;

/// <summary>Свои наборы прав (этап 4E); встроенные — роли, живут в коде (ProjectRoles).</summary>
public interface IPermissionSetRepository
{
    Task<PermissionSet?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Все по имени.</summary>
    Task<IReadOnlyList<PermissionSet>> GetAllAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<PermissionSet>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(PermissionSet set);

    void Remove(PermissionSet set);
}
