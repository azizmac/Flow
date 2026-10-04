using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitRepositoryCatalog : IGitRepositoryCatalog
{
    public List<GitRepository> Items { get; } = [];

    public Task<IReadOnlyList<GitRepository>> GetAllAsync(Guid? connectionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitRepository>>(Items.Where(r => connectionId == null || r.ConnectionId == connectionId).ToList());

    public Task<GitRepository?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(r => r.Id == id));

    public Task<GitRepository?> FindAsync(Guid connectionId, string externalId, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(r => r.ConnectionId == connectionId && r.ExternalId == externalId));

    public void Add(GitRepository repository) => Items.Add(repository);

    public void Remove(GitRepository repository) => Items.Remove(repository);
}
