using Flow.Application.Abstractions;
using Flow.Domain.Entities.GitIntegration;

namespace Flow.Application.Tests.Fakes;

public sealed class FakeGitRepositories
{
    public FakeGitHostConnectionRepository HostConnections { get; } = new();
    public List<GitHostConnection> Connections => HostConnections.Items;

    public FakeGitRepositoryCatalog Catalog { get; } = new();
    public List<GitRepository> Repositories => Catalog.Items;

    public FakeGitRepositoryBoardRepository RepositoryBoards { get; } = new();
    public List<GitRepositoryBoard> Bindings => RepositoryBoards.Items;

    public FakeGitDevelopmentLinkRepository DevelopmentLinks { get; } = new();
    public List<GitDevelopmentLink> Links => DevelopmentLinks.Items;

    public FakeGitIntegrationJobRepository IntegrationJobs { get; } = new();
    public List<GitIntegrationJob> Jobs => IntegrationJobs.Items;
}
