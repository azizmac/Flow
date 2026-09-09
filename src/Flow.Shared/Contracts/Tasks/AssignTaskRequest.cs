namespace Flow.Shared.Contracts.Tasks;

/// <summary>UserId = null — снять исполнителя.</summary>
public sealed record AssignTaskRequest(Guid? UserId);
