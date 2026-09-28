namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// PATCH-семантика: null — не трогать. IsDefault принимает только true (флаг переносится на этот тип;
/// снять его можно, только назначив другой). IsArchived = false возвращает тип из архива.
/// </summary>
public sealed record UpdateTaskTypeRequest(string? Name = null, bool? IsDefault = null, bool? IsArchived = null);
