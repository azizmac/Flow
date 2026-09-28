using Flow.Application.Abstractions;
using Flow.Domain.Entities;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeSavedFilterRepository : ISavedFilterRepository
{
    private readonly List<SavedFilter> _filters = [];
    private readonly List<SavedFilterStar> _stars = [];

    public Task<SavedFilter?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_filters.SingleOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<SavedFilter>> GetVisibleAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SavedFilter>>(_filters.Where(f => f.IsVisibleTo(userId)).OrderBy(f => f.Name).ToList());

    public Task<IReadOnlySet<Guid>> GetStarredIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<Guid>>(_stars.Where(s => s.UserId == userId).Select(s => s.FilterId).ToHashSet());

    public Task<SavedFilterStar?> GetStarAsync(Guid filterId, Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(_stars.SingleOrDefault(s => s.FilterId == filterId && s.UserId == userId));

    public void Add(SavedFilter filter) => _filters.Add(filter);

    public void Remove(SavedFilter filter)
    {
        _filters.Remove(filter);
        _stars.RemoveAll(s => s.FilterId == filter.Id);
    }

    public void AddStar(SavedFilterStar star) => _stars.Add(star);

    public void RemoveStar(SavedFilterStar star) => _stars.Remove(star);
}
