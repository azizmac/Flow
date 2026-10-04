using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitRepositoryBoardRepository : IGitRepositoryBoardRepository
{
    public List<GitRepositoryBoard> Items { get; } = [];

    public Task<IReadOnlyList<GitRepositoryBoard>> GetAsync(Guid? boardId, Guid? repositoryId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GitRepositoryBoard>>(Items.Where(b => (boardId == null || b.BoardId == boardId) && (repositoryId == null || b.RepositoryId == repositoryId)).ToList());

    public void Add(GitRepositoryBoard binding) => Items.Add(binding);

    public void Remove(GitRepositoryBoard binding) => Items.Remove(binding);
}
