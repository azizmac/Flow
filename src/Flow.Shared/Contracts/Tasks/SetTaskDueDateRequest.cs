namespace Flow.Shared.Contracts.Tasks;

/// <summary>DueDate = null — снять срок.</summary>
public sealed record SetTaskDueDateRequest(DateOnly? DueDate);
