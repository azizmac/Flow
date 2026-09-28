namespace Flow.Shared.Contracts.CodeRepositories;

public sealed record CreateCodeRepositoryRequest(string Provider, string Name, string RemoteUrl, string Branch);
