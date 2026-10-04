namespace Flow.Infrastructure.Repositories;

public sealed class RepositoryWorkspaceOptions
{
    public const string SectionName = "Repositories";

    public string WorkspaceRoot { get; set; } = "./repository-workspaces";

    public string AgentWorkspaceRoot { get; set; } = "/workspaces";

    public int GitTimeoutSeconds { get; set; } = 180;
}
