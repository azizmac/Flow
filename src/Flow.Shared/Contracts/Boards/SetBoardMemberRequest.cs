namespace Flow.Shared.Contracts.Boards;

/// <summary>PUT /boards/{id}/members/{userId}: добавить участника или сменить его роль.</summary>
public sealed record SetBoardMemberRequest(ProjectRole Role);
