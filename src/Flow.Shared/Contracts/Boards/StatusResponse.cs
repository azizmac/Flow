namespace Flow.Shared.Contracts.Boards;

/// <summary>SortOrder намеренно не выставлен наружу: массив statuses уже отсортирован сервером.</summary>
public sealed record StatusResponse(Guid Id, string Name, bool IsInitial, bool IsFinal, StatusType? Type);
