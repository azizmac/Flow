namespace Flow.Shared.Contracts.Boards;

/// <summary>PUT /boards/{id}/visibility.</summary>
public sealed record SetVisibilityRequest(BoardVisibility Visibility);
