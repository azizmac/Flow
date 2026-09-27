namespace Flow.Shared.Contracts.Tasks;

/// <summary>TypeId = null — тип проекта по умолчанию; Priority = null — None.</summary>
public sealed record CreateTaskRequest(string Title, string? Description, Guid? StatusId, Guid? TypeId = null, TaskPriority? Priority = null);
