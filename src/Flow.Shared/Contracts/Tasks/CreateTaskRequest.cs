using Flow.Shared.Ids;

namespace Flow.Shared.Contracts.Tasks;

public sealed record CreateTaskRequest(string Title, string? Description, StatusId? StatusId);
