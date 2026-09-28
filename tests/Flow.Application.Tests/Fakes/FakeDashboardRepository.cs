using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeDashboardRepository : IDashboardRepository
{
    private readonly List<Dashboard> _dashboards = [];

    public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_dashboards.SingleOrDefault(d => d.Id == id));

    public Task<IReadOnlyList<Dashboard>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Dashboard>>(_dashboards.Where(d => d.IsVisibleTo(userId)).OrderBy(d => d.Name).ToList());

    public Task<IReadOnlyList<Dashboard>> GetOwnedAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Dashboard>>(_dashboards.Where(d => d.OwnerId == userId).ToList());

    public void Add(Dashboard dashboard) => _dashboards.Add(dashboard);

    public void Remove(Dashboard dashboard) => _dashboards.Remove(dashboard);
}
