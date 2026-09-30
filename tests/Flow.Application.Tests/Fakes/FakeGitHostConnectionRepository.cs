using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitHostConnectionRepository : IGitHostConnectionRepository
{
    public List<GitHostConnection> Items { get; } = [];

    public Task<IReadOnlyList<GitHostConnection>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitHostConnection>>(Items.OrderBy(c => c.Name).ToList());

    public Task<GitHostConnection?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(c => c.Id == id));

    public void Add(GitHostConnection connection) => Items.Add(connection);

    public void Remove(GitHostConnection connection) => Items.Remove(connection);
}
