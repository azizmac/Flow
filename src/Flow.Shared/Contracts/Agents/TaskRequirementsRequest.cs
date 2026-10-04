namespace Flow.Shared.Contracts.Agents;

/// <summary>Текущий черновик задачи для анализа требований по кодовым базам проекта.</summary>
public sealed record TaskRequirementsRequest(Guid BoardId, string Title, string Description, Guid? TaskId = null);
