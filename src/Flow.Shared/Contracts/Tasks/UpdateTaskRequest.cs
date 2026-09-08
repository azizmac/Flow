namespace Flow.Shared.Contracts.Tasks;

/// <summary>PATCH-семантика: заполненные поля меняются, null — не трогать.</summary>
public sealed record UpdateTaskRequest(string? Title, string? Description, Guid? StatusId);
