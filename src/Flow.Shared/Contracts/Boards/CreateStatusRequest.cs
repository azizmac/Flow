namespace Flow.Shared.Contracts.Boards;

/// <summary>Новый статус встаёт в конец списка. Type — вид для фильтров во всех проектах, null — без вида.</summary>
public sealed record CreateStatusRequest(string Name, StatusType? Type = null, bool IsFinal = false);
