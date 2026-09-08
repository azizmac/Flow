namespace Flow.Shared.Contracts.Tasks;

public sealed record CreateTaskRequest(string Title, string? Description, Guid? StatusId);
