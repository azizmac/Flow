using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakePermissionSetRepository : IPermissionSetRepository
{
    public List<PermissionSet> Items { get; } = [];

    public Task<PermissionSet?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Items.FirstOrDefault(s => s.Id == id));

    public Task<IReadOnlyList<PermissionSet>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionSet>>(Items.OrderBy(s => s.Name).ToList());

    public Task<IReadOnlyList<PermissionSet>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionSet>>(Items.Where(s => ids.Contains(s.Id)).ToList());

    public void Add(PermissionSet set) => Items.Add(set);

    /// <summary>Как SetNull в БД: участия удалённого набора возвращаются к правам роли — у фейка набор просто не находится.</summary>
    public void Remove(PermissionSet set) => Items.Remove(set);
}
