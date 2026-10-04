namespace Flow.Shared.Contracts.Agents;

/// <summary>Рекомендации агента и ревизии кодовых баз, доступные во время анализа.</summary>
public sealed record TaskRequirementsResponse(
    string SessionId,
    string Text,
    IReadOnlyList<TaskRequirementsRepository> Repositories,
    IReadOnlyList<string> SkippedRepositories);

public sealed record TaskRequirementsRepository(Guid RepositoryId, string FullName, string Commit);
