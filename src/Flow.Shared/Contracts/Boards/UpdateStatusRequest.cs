namespace Flow.Shared.Contracts.Boards;

/// <summary>
/// PATCH-семантика: null — не трогать. IsInitial принимает только true (флаг переносится на этот статус).
/// Type задаёт вид, ClearType = true снимает его; вместе — 400. WipLimit — мягкий лимит колонки канбана (1…999),
/// ClearWipLimit = true снимает его.
/// </summary>
public sealed record UpdateStatusRequest(
    string? Name = null,
    bool? IsFinal = null,
    bool? IsInitial = null,
    StatusType? Type = null,
    bool ClearType = false,
    int? WipLimit = null,
    bool ClearWipLimit = false);
