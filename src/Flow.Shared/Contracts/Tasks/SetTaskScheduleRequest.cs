namespace Flow.Shared.Contracts.Tasks;

/// <summary>Обе даты задаются вместе, null — снять. StartDate позже DueDate — 400.</summary>
public sealed record SetTaskScheduleRequest(DateOnly? StartDate, DateOnly? DueDate);
