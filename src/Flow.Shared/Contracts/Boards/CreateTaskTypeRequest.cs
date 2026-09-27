namespace Flow.Shared.Contracts.Boards;

public sealed record CreateTaskTypeRequest(string Name, TaskTypeKind Kind, bool IsDefault = false);
